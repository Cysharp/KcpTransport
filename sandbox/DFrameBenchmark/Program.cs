#pragma warning disable CS1998

using DFrame;
using Grpc.Net.Client;
using KcpTransport;
using System.Net;
using System.Text;

var serverTask = KcpServer.RunEcho();

var builder = DFrameApp.CreateBuilder(7312, 7313);

builder.ConfigureWorker(x =>
{
    x.VirtualProcess = 16;
    x.BatchRate = 1;
});

builder.Run(); // WebUI:7312, WorkerListen:7313

await serverTask;

public class KcpServer
{
    public static async Task RunEcho()
    {
        var listener = await KcpListener.ListenAsync(new KcpListenerOptions
        {
            ListenEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5027),
            // EventLoopCount = 1
        });

        while (true)
        {
            var conn = await listener.AcceptConnectionAsync();
            ConsumeClient(await conn.OpenOutboundStreamAsync());
        }

        static async void ConsumeClient(KcpStream stream)
        {
            try
            {
                var buffer = new byte[1024];
                while (true)
                {
                    var len = await stream.ReadAsync(buffer);

                    var str = Encoding.UTF8.GetString(buffer, 0, len);

                    await stream.WriteAsync(Encoding.UTF8.GetBytes(str));
                    //await stream.WriteUnreliableAsync(Encoding.UTF8.GetBytes(str));
                }
            }
            catch (KcpDisconnectedException)
            {
                // await Console.Out.WriteLineAsync("Disconnected");
            }
        }
    }
}

public class KcpTest : Workload
{
    KcpConnection connection = default!;
    KcpStream stream = default!;
    byte[] buf = new byte[1024];
    byte[] msg = "Hello World"u8.ToArray();

    public override async Task SetupAsync(WorkloadContext context)
    {
        connection = await KcpConnection.ConnectAsync("127.0.0.1", 5027);
        stream = await connection.OpenOutboundStreamAsync();
    }

    public override async Task ExecuteAsync(WorkloadContext context)
    {
        stream.Write("Hello World"u8);
        stream.Flush();
        var len = await stream.ReadAsync(buf, context.CancellationToken);

        // Console.WriteLine(Encoding.UTF8.GetString(buf.AsSpan(0, len)));
    }

    public override async Task TeardownAsync(WorkloadContext context)
    {
        if (connection != null)
        {
            connection.Dispose();
        }
    }
}

