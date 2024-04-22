using DFrame;
using KcpTransport;
using System.Net;

namespace DFrameBenchmark.Workloads;

public class KcpServer
{
    public static async Task RunEchoAsync()
    {
        var listener = await KcpListener.ListenAsync(new KcpListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5027),
            // EventLoopCount = 1
        });

        while (true)
        {
            var conn = await listener.AcceptConnectionAsync();
            ConsumeClient(conn);
        }

        static async void ConsumeClient(KcpConnection connection)
        {
            using (connection)
            using (var stream = await connection.OpenOutboundStreamAsync())
            {
                try
                {
                    var buffer = new byte[1024];
                    while (true)
                    {
                        var len = await stream.ReadAsync(buffer);
                        if (len == 0)
                        {
                            return;
                        }
                        await stream.WriteAsync(buffer.AsMemory(0, len));
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

public class KcpWorkload : Workload
{
    byte[] msg = "Hello World"u8.ToArray();

    KcpConnection connection = default!;
    KcpStream stream = default!;
    byte[] buffer = new byte[1024];

    public override async Task SetupAsync(WorkloadContext context)
    {
        connection = await KcpConnection.ConnectAsync("127.0.0.1", 5027);
        stream = await connection.OpenOutboundStreamAsync();
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        stream.Write(msg.AsSpan());
        stream.Flush();
        var len = await stream.ReadAsync(buffer, context.CancellationToken);
        if (!buffer.AsSpan(0, len).SequenceEqual(msg))
        {
            throw new Exception("Invalid Response.");
        }
    }

    public override async Task TeardownAsync(WorkloadContext context)
    {
        stream?.Dispose();
        connection?.Dispose();
    }
}

public class KcpUnreliableWorkload : Workload
{
    byte[] msg = "Hello World"u8.ToArray();

    KcpConnection connection = default!;
    KcpStream stream = default!;
    byte[] buffer = new byte[1024];

    public override async Task SetupAsync(WorkloadContext context)
    {
        connection = await KcpConnection.ConnectAsync("127.0.0.1", 5027);
        stream = await connection.OpenOutboundStreamAsync();
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        stream.WriteUnreliable(msg.AsSpan());
        stream.Flush();
        var len = await stream.ReadAsync(buffer, context.CancellationToken);
        if (!buffer.AsSpan(0, len).SequenceEqual(msg))
        {
            throw new Exception("Invalid Response.");
        }
    }

    public override async Task TeardownAsync(WorkloadContext context)
    {
        stream?.Dispose();
        connection?.Dispose();
    }
}

