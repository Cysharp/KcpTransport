using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace KcpTransport;

public sealed class KcpListener : IDisposable
{
    Socket socket;
    Channel<KcpConnection> acceptQueue;
    ConcurrentDictionary<uint, KcpConnection> connections = new();
    readonly long startingTimestamp = Stopwatch.GetTimestamp();

    public static ValueTask<KcpListener> ListenAsync(int listenPort)
    {
        // sync but for future extensibility.
        return new ValueTask<KcpListener>(new KcpListener(listenPort));
    }

    KcpListener(int listenPort)
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); // in Linux, as SO_REUSEPORT

        var endPoint = new IPEndPoint(IPAddress.Any, listenPort);
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

        this.StartSocketEventLoop(endPoint);
    }

    public ValueTask<KcpConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
    {
        return acceptQueue.Reader.ReadAsync(cancellationToken);
    }

    async void StartSocketEventLoop(IPEndPoint serverEndPoint, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var receivedAddress = new SocketAddress(AddressFamily.InterNetwork);
        var random = new Random();

        // TODO: mtu size
        var socketBuffer = GC.AllocateUninitializedArray<byte>(1500, pinned: true); // Create to pinned object heap

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
                        var kcpConnection = new KcpConnection(conversationId, serverEndPoint, receivedAddress);
                        connections[conversationId] = kcpConnection;
                        acceptQueue.Writer.TryWrite(kcpConnection);
                        SendHandshakeResponse(socket, conversationId, receivedAddress);
                    }
                    break;
                case PacketType.Unreliable:
                    {
                        conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4));
                        if (!connections.TryGetValue(conversationId, out var kcpConnection))
                        {
                            // may incoming old packet, TODO: log it.
                            continue;
                        }

                        kcpConnection.WriteRawBuffer(socketBuffer.AsSpan(8));
                        await kcpConnection.StreamFlushAsync(cancellationToken);
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
                            if (!kcpConnection.InputReceivedBuffer(socketBufferPointer, received)) continue;
                        }

                        if (kcpConnection.ConsumeKcpFragments(receivedAddress))
                        {
                            // TODO: don't (a)wait in this loop!
                            await kcpConnection.StreamFlushAsync(cancellationToken);
                        }
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

    public void UpdateConnections()
    {
        var elapsed = Stopwatch.GetElapsedTime(startingTimestamp);
        foreach (var connection in connections.Values) // TODO: Dictionary iteration is slow. need more.
        {
            if (connection != null)
            {
                connection.KcpUpdateTimestamp((uint)elapsed.TotalMilliseconds);
            }
        }
    }

    public unsafe void Dispose()
    {
        // TODO:connections dispose?
        socket.Dispose();
    }
}
