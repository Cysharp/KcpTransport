using DFrame;
using System.Net;
using System.Net.Sockets;

namespace DFrameBenchmark.Workloads;

public class TcpServer
{
    public static async Task RunEchoAsync(CancellationToken cancellationToken)
    {
        using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Blocking = false;
        socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5027));
        socket.Listen();

        while (!cancellationToken.IsCancellationRequested)
        {
            var conn = await socket.AcceptAsync(cancellationToken);
            ConsumeClient(conn, cancellationToken);
        }

        static async void ConsumeClient(Socket socket, CancellationToken cancellationToken)
        {
            using (socket)
            {
                try
                {
                    var buffer = new byte[1024];
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var len = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                        if (len == 0)
                        {
                            return; // end.
                        }
                        await socket.SendAsync(buffer.AsMemory(0, len), cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }
}

public class TcpWorkload : Workload
{
    byte[] msg = "Hello World"u8.ToArray();

    Socket socket = default!;
    byte[] buffer = new byte[1024];

    public override async Task SetupAsync(WorkloadContext context)
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Blocking = false;
        await socket.ConnectAsync(IPAddress.Parse("127.0.0.1"), 5027);
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        await socket.SendAsync(msg.AsMemory(), context.CancellationToken);
        var len = await socket.ReceiveAsync(buffer.AsMemory(), context.CancellationToken);
        if (!buffer.AsSpan(0, len).SequenceEqual(msg))
        {
            throw new Exception("Invalid Response.");
        }
    }

    public override async Task TeardownAsync(WorkloadContext context)
    {
        await socket.DisconnectAsync(false);
        socket?.Dispose();
    }
}

