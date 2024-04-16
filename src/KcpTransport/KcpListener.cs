using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using static KcpTransport.LowLevel.KcpMethods;

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
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Any, listenPort));
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

        this.StartSocketEventLoop();
    }

    public ValueTask<KcpConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
    {
        return acceptQueue.Reader.ReadAsync(cancellationToken);
    }

    async void StartSocketEventLoop(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        var receivedAddress = new SocketAddress(AddressFamily.InterNetwork);
        var receivedAddress2 = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 0); // TODO:use Socketaddress

        // TODO: mtu size
        var socketBuffer = GC.AllocateUninitializedArray<byte>(1500, pinned: true); // Create to pinned object heap

        while (true)
        {
            // Socket is datagram so received contains full block
            // var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress, cancellationToken);
            var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress2, cancellationToken);
            unsafe
            {
                var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));

                // TODO: use MemoryMarshal.Read<uint>, simply first 4 byte is conversation id
                var conversationId = ikcp_getconv(socketBufferPointer);
                if (!connections.TryGetValue(conversationId, out var kcpConnection))
                {
                    kcpConnection = new KcpConnection(conversationId, receivedAddress2, socket);
                    connections[conversationId] = kcpConnection;
                    acceptQueue.Writer.TryWrite(kcpConnection);
                }

                if (!kcpConnection.InputReceivedBuffer(socketBufferPointer, received.ReceivedBytes)) continue;
                kcpConnection.ConsumeKcpFragments((IPEndPoint)received.RemoteEndPoint);
            }
        }
    }

    public void UpdateConnections()
    {
        foreach (var connection in connections.Values)
        {
            var ts = Stopwatch.GetTimestamp();
            connection.KcpUpdateTimestamp((uint)ts);
        }
    }

    public unsafe void Dispose()
    {
        // TODO:connections dispose?
        socket.Dispose();
    }
}
