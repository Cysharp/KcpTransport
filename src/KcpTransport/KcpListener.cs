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

        // TODO: mtu size
        var socketBuffer = GC.AllocateUninitializedArray<byte>(1500, pinned: true); // Create to pinned object heap

        while (true)
        {
            // Socket is datagram so received contains full block
            // var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress, cancellationToken);
            var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress, cancellationToken);
            KcpConnection? kcpConnection;
            unsafe
            {
                var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));
                // peek kcp data, simply first 4 byte is conversation id
                var conversationId = MemoryMarshal.Read<uint>(socketBuffer);

                if (!connections.TryGetValue(conversationId, out kcpConnection))
                {
                    kcpConnection = new KcpConnection(conversationId, serverEndPoint, receivedAddress);
                    connections[conversationId] = kcpConnection;
                    acceptQueue.Writer.TryWrite(kcpConnection);
                }

                if (!kcpConnection.InputReceivedBuffer(socketBufferPointer, received)) continue;
            }

            if (kcpConnection.ConsumeKcpFragments(receivedAddress))
            {
                await kcpConnection.StreamFlushAsync(cancellationToken);
            }
        }
    }

    public void UpdateConnections()
    {
        foreach (var connection in connections.Values)
        {
            var ts = Stopwatch.GetTimestamp(); // TODO: NG.
            connection.KcpUpdateTimestamp((uint)ts);
        }
    }

    public unsafe void Dispose()
    {
        // TODO:connections dispose?
        socket.Dispose();
    }
}
