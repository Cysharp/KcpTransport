using KcpTransport;
using KcpTransport.LowLevel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static KcpTransport.LowLevel.KcpMethods;

var server = RunEchoServer();
var client = RunEchoClient();

await await Task.WhenAny(server, client);

static async Task RunEchoServer()
{
    // Create KCP Server
    var listener = await KcpListener.ListenAsync("127.0.0.1", 11000);

    // Accept client connection loop
    while (true)
    {
        var connection = await listener.AcceptConnectionAsync();
        ConsumeClient(connection);
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
                    // Wait incoming data
                    var len = await stream.ReadAsync(buffer);

                    var str = Encoding.UTF8.GetString(buffer, 0, len);
                    Console.WriteLine("Server Request  Received: " + str);

                    // Send to Client(KCP, Reliable)
                    await stream.WriteAsync(Encoding.UTF8.GetBytes(str));

                    // Send to Client(Unreliable)
                    //await stream.WriteUnreliableAsync(Encoding.UTF8.GetBytes(str));
                }
            }
            catch (KcpDisconnectedException)
            {
                // when client has been disconnected, ReadAsync will throw KcpDisconnectedException
                Console.WriteLine($"Disconnected, Id:{connection.ConnectionId}");
            }
        }
    }
}

static async Task RunEchoClient()
{
    // Create KCP Client
    using var connection = await KcpConnection.ConnectAsync("127.0.0.1", 11000);
    using var stream = await connection.OpenOutboundStreamAsync();

    var buffer = new byte[1024];
    while (true)
    {
        Console.Write("Input Text:");
        var inputText = Console.ReadLine();

        // Send to Server(KCP, Reliable), or use WriteUnreliableAsync
        await stream.WriteAsync(Encoding.UTF8.GetBytes(inputText!));

        // Wait server response
        var len = await stream.ReadAsync(buffer);

        var str = Encoding.UTF8.GetString(buffer, 0, len);

        Console.WriteLine($"Client Response Received: " + str);
    }
}

public class SampleLowLevel : IDisposable
{
    GCHandle user;
    unsafe IKCPCB* kcp;
    bool isDisposed;
    readonly long startingTimestamp;

    // void* is user, you can cast by GCHandle.FromIntPtr((IntPtr)ptr).Target
    public unsafe SampleLowLevel(delegate* managed<byte*, int, IKCPCB*, void*, int> output, object user)
    {
        this.user = GCHandle.Alloc(this);
        this.kcp = ikcp_create(conv: 0, user: (void*)GCHandle.ToIntPtr(this.user));
        ikcp_setoutput(kcp, output);
        this.startingTimestamp = Stopwatch.GetTimestamp();
        Update();
    }

    public unsafe int Send(ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data)
        {
            return ikcp_send(kcp, ptr, data.Length);
        }
    }

    public unsafe int InputData(ReadOnlySpan<byte> data)
    {
        fixed (byte* ptr = data)
        {
            return ikcp_input(kcp, ptr, data.Length);
        }
    }

    public unsafe int PeekSize()
    {
        return ikcp_peeksize(kcp);
    }

    public unsafe int ReceiveData(Span<byte> buffer)
    {
        fixed (byte* ptr = buffer)
        {
            return ikcp_recv(kcp, ptr, buffer.Length);
        }
    }

    public unsafe void Update()
    {
        var elapsed = Stopwatch.GetElapsedTime(startingTimestamp);
        var currentTimestampMilliseconds = (uint)elapsed.TotalMilliseconds;
        ikcp_update(kcp, currentTimestampMilliseconds);
    }

    public unsafe void Flush()
    {
        ikcp_flush(kcp);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!isDisposed)
        {
            if (disposing)
            {
                // cleanup managed.
            }

            // cleanup unmanaged.
            unsafe
            {
                user.Free();
                user = default;
                ikcp_release(kcp);
                kcp = null;
            }

            isDisposed = true;
        }
    }

    ~SampleLowLevel()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
