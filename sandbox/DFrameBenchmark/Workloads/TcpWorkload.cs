using DFrame;
using System.Net;
using System.Net.Sockets;

namespace DFrameBenchmark.Workloads;

public class TcpServer
{
    public static async Task RunEchoAsync()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Blocking = false;
        socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5027));
        socket.Listen();

        while (true)
        {
            var conn = await socket.AcceptAsync(CancellationToken.None);
            ConsumeClient(conn);
        }

        static async void ConsumeClient(Socket socket)
        {
            var timeout = new CancellationTokenSource();
            using (socket)
            {
                try
                {
                    var buffer = new byte[1024];
                    while (true)
                    {
                        timeout.TryReset();
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        var len = await socket.ReceiveAsync(buffer.AsMemory(), timeout.Token);
                        if (len == 0)
                        {
                            return; // end.
                        }
                        await socket.SendAsync(buffer.AsMemory(0, len), timeout.Token);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
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
        await socket.SendAsync(msg.AsMemory());
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

