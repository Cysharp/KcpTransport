using KcpTransport.LowLevel;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace KcpTransport;

public abstract record class KcpOptions
{
    public bool EnableNoDelay { get; set; } = true;
    public int IntervalMilliseconds { get; set; } = 10; // ikcp_nodelay min is 10.
    public int Resend { get; set; } = 2;
    public bool EnableFlowControl { get; set; } = false;
    public (int SendWindow, int ReceiveWindow) WindowSize { get; set; } = ((int)KcpMethods.IKCP_WND_SND, (int)KcpMethods.IKCP_WND_RCV);
    public int MaximumTransmissionUnit { get; set; } = (int)KcpMethods.IKCP_MTU_DEF;
    // public int MinimumRetransmissionTimeout { get; set; } this value is changed in ikcp_nodelay(and use there default) so no configurable.
}

public sealed record class KcpListenerOptions : KcpOptions
{
    public required IPEndPoint ListenEndPoint { get; set; }
    public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromMilliseconds(5);
    public int EventLoopCount { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public bool ConfigureAwait { get; set; } = false;
}

public sealed class KcpListener : IDisposable
{
    Socket socket;
    Channel<KcpConnection> acceptQueue;

    ConcurrentDictionary<uint, KcpConnection> connections = new();

    Task[] socketEventLoopTasks;
    Thread updateConnectionsWorkerThread;
    CancellationTokenSource listenerCancellationTokenSource = new();

    public static ValueTask<KcpListener> ListenAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        return ListenAsync(new IPEndPoint(IPAddress.Parse(host), port), cancellationToken);
    }

    public static ValueTask<KcpListener> ListenAsync(IPEndPoint listenEndPoint, CancellationToken cancellationToken = default)
    {
        return ListenAsync(new KcpListenerOptions { ListenEndPoint = listenEndPoint }, cancellationToken);
    }

    public static ValueTask<KcpListener> ListenAsync(KcpListenerOptions options, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // sync but for future extensibility.
        return new ValueTask<KcpListener>(new KcpListener(options));
    }

    KcpListener(KcpListenerOptions options)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); // in Linux, as SO_REUSEPORT

        var endPoint = options.ListenEndPoint;
        try
        {
            socket.Bind(endPoint);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        this.socket = socket;
        this.acceptQueue = Channel.CreateUnbounded<KcpConnection>(new UnboundedChannelOptions()
        {
            SingleWriter = true
        });

        this.socketEventLoopTasks = new Task[options.EventLoopCount];
        for (int i = 0; i < socketEventLoopTasks.Length; i++)
        {
            socketEventLoopTasks[i] = this.StartSocketEventLoopAsync(options, i);
        }

        updateConnectionsWorkerThread = new Thread(RunUpdateKcpConnectionLoop)
        {
            Name = $"{nameof(KcpListener)}",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        updateConnectionsWorkerThread.Start(options.UpdatePeriod);
    }

    public ValueTask<KcpConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
    {
        return acceptQueue.Reader.ReadAsync(cancellationToken);
    }

    async Task StartSocketEventLoopAsync(KcpListenerOptions options, int id)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var receivedAddress = new SocketAddress(options.ListenEndPoint.AddressFamily);
        var random = new Random();
        var cancellationToken = this.listenerCancellationTokenSource.Token;

        // TODO: mtu size
        var socketBuffer = GC.AllocateUninitializedArray<byte>(1400, pinned: true); // Create to pinned object heap

        while (true)
        {
            // Socket is datagram so received data contains full block
            var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress, cancellationToken);

            // first 4 byte is conversationId or extra packet type
            var conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(0, received));
            var packetType = (PacketType)conversationId;
            switch (packetType)
            {
                case PacketType.Handshake:
                ISSUE_CONVERSATION_ID:
                    {
                        conversationId = unchecked((uint)random.Next(100, int.MaxValue)); // 0~99 is reserved
                        if (!connections.TryAdd(conversationId, null!)) // reserve dictionary space
                        {
                            goto ISSUE_CONVERSATION_ID;
                        }

                        // create new connection
                        var kcpConnection = new KcpConnection(conversationId, options, receivedAddress);
                        connections[conversationId] = kcpConnection;
                        acceptQueue.Writer.TryWrite(kcpConnection);
                        SendHandshakeResponse(socket, conversationId, receivedAddress);
                    }
                    break;
                case PacketType.Unreliable:
                    {
                        conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4, received - 4));
                        if (!connections.TryGetValue(conversationId, out var kcpConnection))
                        {
                            // may incoming old packet, TODO: log it.
                            continue;
                        }

                        kcpConnection.InputReceivedUnreliableBuffer(socketBuffer.AsSpan(8, received - 8));

                        await kcpConnection.AwaitForLastFlushResult();
                        kcpConnection.FlushReceivedBuffer(cancellationToken);
                    }
                    break;
                default:
                    {
                        // Reliable
                        if (conversationId < 100)
                        {
                            // may incoming invalid packet, TODO: log it.
                            continue;
                        }
                        if (!connections.TryGetValue(conversationId, out var kcpConnection))
                        {
                            // may incoming old packet, TODO: log it.
                            continue;
                        }

                        unsafe
                        {
                            var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));
                            if (!kcpConnection.InputReceivedKcpBuffer(socketBufferPointer, received)) continue;
                        }

                        await kcpConnection.AwaitForLastFlushResult();
                        kcpConnection.ConsumeKcpFragments(receivedAddress, cancellationToken);
                    }
                    break;
            }
        }

        static void SendHandshakeResponse(Socket socket, uint conversationId, SocketAddress clientAddress)
        {
            Span<byte> data = stackalloc byte[4];
            MemoryMarshal.Write<uint>(data, conversationId);
            socket.SendTo(data, SocketFlags.None, clientAddress);
        }
    }

    void RunUpdateKcpConnectionLoop(object? state)
    {
        // NOTE: should use ikcp_check? https://github.com/skywind3000/kcp/wiki/EN_KCP-Best-Practice#advance-update

        // All Windows(.NET) Timer and Sleep is low-resolution(min is 16ms).
        // We use custom high-resolution timer instead.

        var cancellationToken = listenerCancellationTokenSource.Token;
        var period = (TimeSpan)state!;
        var waitTime = (int)period.TotalMilliseconds;

        while (true)
        {
            SleepInterop.Sleep(waitTime);

            if (cancellationToken.IsCancellationRequested) break;

            foreach (var kvp in connections)
            {
                var connection = kvp.Value;
                if (connection != null) // at handshake, connection is not created yet so sometimes null...
                {
                    connection.UpdateKcp();
                }
            }
        }
    }

    public unsafe void Dispose()
    {
        // TODO:connections dispose?

        acceptQueue.Writer.Complete();

        listenerCancellationTokenSource.Cancel();
        listenerCancellationTokenSource.Dispose();

        socket.Dispose();
        foreach (var connection in connections)
        {
            connection.Value.Dispose();
        }
    }
}
