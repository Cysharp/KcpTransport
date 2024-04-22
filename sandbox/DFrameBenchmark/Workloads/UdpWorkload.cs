using DFrame;
using System.Net;
using System.Net.Sockets;

namespace DFrameBenchmark.Workloads;

public class UdpServer
{
    public static async Task RunEchoAsync()
    {
        Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Blocking = false;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        if (OperatingSystem.IsWindows())
        {
            const uint IOC_IN = 0x80000000U;
            const uint IOC_VENDOR = 0x18000000U;
            const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            socket.IOControl(unchecked((int)SIO_UDP_CONNRESET), [0x00], null);
        }

        socket.Bind(new IPEndPoint(IPAddress.Parse("127.0.0.1"), 5027));

        var buffer = new byte[1400];
        var address = new SocketAddress(AddressFamily.InterNetwork);
        while (true)
        {
            var len = await socket.ReceiveFromAsync(buffer.AsMemory(), SocketFlags.None, address);
            await socket.SendToAsync(buffer.AsMemory(0, len), SocketFlags.None, address);
        }
    }
    
    public static async Task RunEchoMultiAsync()
    {
        List<Task> tasks = new();

        var engineCount = Environment.ProcessorCount / 2;
        for (int i = 0; i < engineCount; i++)
        {
            tasks.Add(RunEchoAsync());
        }

        await await Task.WhenAny(tasks);
    }
}

public class UdpWorkload : Workload
{
    byte[] msg = "Hello World"u8.ToArray();

    Socket socket = default!;
    byte[] buffer = new byte[1024];

    public override async Task SetupAsync(WorkloadContext context)
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Blocking = false;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        if (OperatingSystem.IsWindows())
        {
            const uint IOC_IN = 0x80000000U;
            const uint IOC_VENDOR = 0x18000000U;
            const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
            socket.IOControl(unchecked((int)SIO_UDP_CONNRESET), [0x00], null);
        }
        socket.Connect(IPAddress.Parse("127.0.0.1"), 5027);
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
        socket?.Dispose();
    }
}
