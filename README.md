# KcpTransport

[![GitHub Actions](https://github.com/Cysharp/KcpTransport/workflows/Build-Debug/badge.svg)](https://github.com/Cysharp/KcpTransport/actions) [![Releases](https://img.shields.io/github/release/Cysharp/KcpTransport.svg)](https://github.com/Cysharp/KcpTransport/releases)
[![NuGet package](https://img.shields.io/nuget/v/KcpTransport.svg)](https://nuget.org/packages/KcpTransport)

KcpTransport is a Pure C# implementation of RUDP for high-performance real-time network communication. Similar to the implementation of [System.Net.Quic](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview), it provides `KcpListener`, `KcpConnection`, and `KcpStream`. All Read/Write Operations are handled in a Stream-based manner, just like [NetworkStream](https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.networkstream) in TCP, providing an easy-to-use and modern asynchronous API that supports async/await. Furthermore, by implementing the [ASP.NET Kestrel Transport](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.server.kestrel.transport.sockets.sockettransportfactory?view=aspnetcore-8.0) in the future, the goal is to enable the replacement of the transport layer of gRPC and [MagicOnion](https://github.com/Cysharp/MagicOnion) with KCP.

> [!CAUTION]
> This library is currently in alpha preview. It cannot be used for production.

## Why KCP?

* Variations of [RUDP](https://en.wikipedia.org/wiki/Reliable_User_Datagram_Protocol) have been widely adopted in applications that require real-time performance, which is difficult to achieve with TCP like gaming.
* [QUIC](https://en.wikipedia.org/wiki/QUIC) is the future, but currently, it has difficulties with multi-platform support (especially for use on game consoles).
* [KCP](https://github.com/skywind3000/kcp/blob/master/README.en.md) has a proven track record in [games, audio, video, and more](https://www.skywind.me/blog/archives/2706), with Genshin Impact being a notable example of its adoption.
* KCP itself has a simple implementation without any system calls, allowing it to be implemented in Pure C# while leveraging the latest UDP Socket Improvements and async/await support in .NET.

KcpTransport is built on top of KCP ported to Pure C#, with implementations of Syn Cookie handshake, connection management, Unreliable communication, and KeepAlive. In the future, encryption will also be supported.

## Getting Started

This library is distributed via NuGet. Currently, it only supports `.NET 8` as it is in preview, but in the future, it plans to support `.NET Standard 2.1` and `Unity`.

PM> Install-Package [KcpTransport](https://www.nuget.org/packages/KcpTransport)

On the server side, `KcpListener.ListenAsync` is used to generate the connection, while on the client side, `KcpConnection.ConnectAsync` is used. The `Stream` for performing Read/Write operations is obtained using `OpenOutboundStreamAsync`.

```csharp
using KcpTransport;
using System.Text;

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
```

Options
---
`KcpListener` and `KcpConnection` can each be passed options when they are created.

```csharp
var listener = await KcpListener.ListenAsync(new KcpListenerOptions
{
    ListenEndPoint = new IPEndPoint(IPAddress.Parse("127.0.0.1"), listenPort),
    EventLoopCount = 1,
    KeepAliveDelay = TimeSpan.FromSeconds(10),
    ConnectionTimeout = TimeSpan.FromSeconds(20),
});
```

Currently, the default values are as follows:

```csharp
public abstract record class KcpOptions
{
    public bool EnableNoDelay { get; set; } = true;
    public int IntervalMilliseconds { get; set; } = 10; // ikcp_nodelay min is 10.
    public int Resend { get; set; } = 2;
    public bool EnableFlowControl { get; set; } = false;
    public (int SendWindow, int ReceiveWindow) WindowSize { get; set; } = ((int)KcpMethods.IKCP_WND_SND, (int)KcpMethods.IKCP_WND_RCV);
    public int MaximumTransmissionUnit { get; set; } = (int)KcpMethods.IKCP_MTU_DEF;
}

public sealed record class KcpListenerOptions : KcpOptions
{
    public required IPEndPoint ListenEndPoint { get; set; }
    public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromMilliseconds(5);
    public int EventLoopCount { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public bool ConfigureAwait { get; set; } = false;
    public TimeSpan KeepAliveDelay { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public HashFunc Handshake32bitHashKeyGenerator { get; set; } = KeyGenerator;
    public Action<Socket, KcpListenerOptions, ListenerSocketType>? ConfigureSocket { get; set; }
}

public sealed record class KcpClientConnectionOptions : KcpOptions
{
    public required EndPoint RemoteEndPoint { get; set; }
    public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromMilliseconds(5);
    public bool ConfigureAwait { get; set; } = false;
    public TimeSpan KeepAliveDelay { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(1);
    public Action<Socket, KcpClientConnectionOptions>? ConfigureSocket { get; set; }
}
```

LowLevel API
---
The KcpTransport provides a low-level API that directly uses the methods from ikcp.c and defined in ikcp.h.

```c
ikcpcb* ikcp_create(IUINT32 conv, void *user);
void ikcp_release(ikcpcb *kcp);
void ikcp_setoutput(ikcpcb *kcp, int (*output)(const char *buf, int len,  ikcpcb *kcp, void *user));
int ikcp_recv(ikcpcb *kcp, char *buffer, int len);
int ikcp_send(ikcpcb *kcp, const char *buffer, int len);
void ikcp_update(ikcpcb *kcp, IUINT32 current);
IUINT32 ikcp_check(const ikcpcb *kcp, IUINT32 current);
int ikcp_input(ikcpcb *kcp, const char *data, long size);
void ikcp_flush(ikcpcb *kcp);
int ikcp_peeksize(const ikcpcb *kcp);
int ikcp_setmtu(ikcpcb *kcp, int mtu);
int ikcp_wndsize(ikcpcb *kcp, int sndwnd, int rcvwnd);
int ikcp_waitsnd(const ikcpcb *kcp);
int ikcp_nodelay(ikcpcb *kcp, int nodelay, int interval, int resend, int nc);
IUINT32 ikcp_getconv(const void *ptr);
```

By using static `KcpTransport.LowLevel.KcpMethods`, you can use the `ikcp_***` functions.

```csharp
using KcpTransport;
using KcpTransport.LowLevel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static KcpTransport.LowLevel.KcpMethods; // use ikcp methods

public class SampleLowLevel : IDisposable
{
    GCHandle user;
    unsafe IKCPCB* kcp;
    bool isDisposed;
    readonly long startingTimestamp;

    // void* is user, you can cast by GCHandle.FromIntPtr((IntPtr)ptr).Target
    public unsafe SampleLowLevel(uint conversationId, delegate* managed<byte*, int, IKCPCB*, void*, int> output, object user)
    {
        this.user = GCHandle.Alloc(this);
        this.kcp = ikcp_create(conv: conversationId, user: (void*)GCHandle.ToIntPtr(this.user));
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
```

License
---
This library is under the MIT License.
