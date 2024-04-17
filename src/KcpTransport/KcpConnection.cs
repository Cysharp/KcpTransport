using KcpTransport.LowLevel;
using System;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static KcpTransport.LowLevel.KcpMethods;

namespace KcpTransport;

public class KcpConnection
{
    unsafe IKCPCB* kcp;
    SocketAddress? remoteAddress;
    KcpStream stream;
    Socket socket;

    // create by User, for client connection
    public unsafe KcpConnection(uint conversationId, IPEndPoint remoteAddress)
    {
        this.kcp = ikcp_create(conversationId, this);
        this.kcp->output = KcpOutputCallback;
        this.kcp->writelog = KcpWriteLog;
        this.socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        this.socket.Connect(remoteAddress); // TODO: connectasync?
        this.stream = new KcpStream(this);

        StartSocketEventLoop(remoteAddress);

        UpdateTimestamp();
    }

    // create from Listerner for server connection
    internal unsafe KcpConnection(uint conversationId, IPEndPoint serverEndPoint, SocketAddress remoteAddress)
    {
        this.kcp = ikcp_create(conversationId, this);
        this.kcp->output = KcpOutputCallback;
        this.kcp->writelog = KcpWriteLog;
        this.remoteAddress = remoteAddress.Clone();

        // bind same port and connect client IP, this socket is used only for Send
        this.socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        this.socket.Bind(serverEndPoint);
        this.socket.Connect(remoteAddress.ToIPEndPoint());

        this.stream = new KcpStream(this);

        UpdateTimestamp();
    }

    async void StartSocketEventLoop(IPEndPoint remoteAddress, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        // TODO: mtu size
        var socketBuffer = GC.AllocateUninitializedArray<byte>(1500, pinned: true); // Create to pinned object heap

        while (true)
        {
            // Socket is datagram so received contains full block
            var received = await socket.ReceiveAsync(socketBuffer, SocketFlags.None, cancellationToken);
            unsafe
            {
                var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));
                InputReceivedBuffer(socketBufferPointer, received);
            }

            if (ConsumeKcpFragments(null))
            {
                await stream.Writer.FlushAsync(cancellationToken);
            }
        }
    }

    internal ValueTask<FlushResult> StreamFlushAsync(CancellationToken cancellationToken)
    {
        return stream.Writer.FlushAsync(cancellationToken);
    }

    public ValueTask<KcpStream> OpenOutboundStreamAsync()
    {
        return new ValueTask<KcpStream>(stream);
    }

    internal unsafe bool InputReceivedBuffer(byte* buffer, int length)
    {
        var inputResult = ikcp_input(kcp, buffer, length);
        if (inputResult == 0)
        {
            return true;
        }
        else
        {
            // TODO: log
            return false;
        }
    }

    internal unsafe bool ConsumeKcpFragments(SocketAddress? remoteAddress)
    {
        var size = ikcp_peeksize(kcp);
        if (size > 0)
        {
            if (remoteAddress != null && !remoteAddress.Equals(this.remoteAddress))
            {
                // TODO: shutdown existing socket and create new one?
            }

            var writer = stream.Writer;
            var buffer = writer.GetSpan(size);

            fixed (byte* p = buffer)
            {
                var len = ikcp_recv(kcp, p, buffer.Length);
                if (len > 0)
                {
                    writer.Advance(len);
                    return true;
                }
            }
        }
        return false;
    }

    internal unsafe void SendBuffer(ReadOnlySpan<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            ikcp_send(kcp, p, buffer.Length);
        }

        // TODO: auto flush?
        Flush();
    }

    internal unsafe void Flush()
    {
        ikcp_flush(kcp);
    }

    public unsafe void UpdateTimestamp()
    {
        KcpUpdateTimestamp((uint)Stopwatch.GetTimestamp());
    }

    internal unsafe void KcpUpdateTimestamp(uint timestamp)
    {
        ikcp_update(kcp, timestamp);
    }

    static unsafe int KcpOutputCallback(byte* buf, int len, IKCPCB* kcp, object user)
    {
        var self = (KcpConnection)user;
        var buffer = new Span<byte>(buf, len);

        var sent = self.socket.Send(buffer);
        return sent;
    }

    static unsafe void KcpWriteLog(string msg, IKCPCB* kcp, object user)
    {
        Console.WriteLine(msg);
    }
}
