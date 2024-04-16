using KcpTransport.LowLevel;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static KcpTransport.LowLevel.KcpMethods;

namespace KcpTransport;

public class KcpConnection
{
    unsafe IKCPCB* kcp;
    //SocketAddress? remoteAddress;
    IPEndPoint? remoteAddress;
    bool connected;
    KcpStream stream;
    Socket socket;

    public unsafe KcpConnection(uint conversationId, IPEndPoint remoteAddress, bool connect = true)
    {
        this.kcp = ikcp_create(conversationId, this);
        this.kcp->output = KcpOutputCallback;
        this.socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        if (connect)
        {
            this.socket.Connect(remoteAddress); // TODO: connectasync?
            StartSocketEventLoop(remoteAddress);
        }
        this.connected = connect;
        this.remoteAddress = remoteAddress;
        this.stream = new KcpStream(this);

        UpdateTimestamp();
    }

    public unsafe KcpConnection(uint conversationId, IPEndPoint remoteAddress, Socket socket) // TODO: thread non-safe
    {
        this.kcp = ikcp_create(conversationId, this);
        this.kcp->output = KcpOutputCallback;
        this.socket = socket;
        this.connected = false;
        this.remoteAddress = remoteAddress;
        this.stream = new KcpStream(this);

        UpdateTimestamp();
    }

    internal unsafe KcpConnection(uint conversationId, SocketAddress remoteAddress)
    {
        this.kcp = ikcp_create(conversationId, this);
        this.kcp->output = KcpOutputCallback;
        // this.remoteAddress = CloneSocketAddress(remoteAddress); // TODO: currently cloned address slows exception when SendTo
        this.socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
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
            // var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress, cancellationToken);
            var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, remoteAddress, cancellationToken);
            unsafe
            {
                var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));

                InputReceivedBuffer(socketBufferPointer, received.ReceivedBytes);
                ConsumeKcpFragments((IPEndPoint)received.RemoteEndPoint);
            }
        }
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

    internal unsafe void ConsumeKcpFragments(IPEndPoint remoteAddress)
    {
        var size = ikcp_peeksize(kcp);
        if (size > 0)
        {
            //if (!connected && !this.remoteAddress!.Equals(remoteAddress))
            {
                // swap latest address(TODO: sercurity?)
                // this.remoteAddress = CloneSocketAddress(remoteAddress);
            }
            this.remoteAddress = remoteAddress; // TODO: swap... 

            var buffer = stream.GetSpan(size); // TODO: lock?

            fixed (byte* p = buffer)
            {
                var len = ikcp_recv(kcp, p, buffer.Length);
                if (len > 0)
                {
                    stream.Advance(len);
                    stream.WriterFlush();
                }
            }
        }
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

    static SocketAddress CloneSocketAddress(SocketAddress socketAddress)
    {
        var clone = new SocketAddress(socketAddress.Family, socketAddress.Size);
        socketAddress.Buffer.CopyTo(clone.Buffer);
        return clone;
    }

    static unsafe int KcpOutputCallback(byte* buf, int len, IKCPCB* kcp, object user)
    {
        var self = (KcpConnection)user;
        var buffer = new Span<byte>(buf, len);

        int sent;
        if (self.connected)
        {
            sent = self.socket.Send(buffer);
        }
        else
        {
            sent = self.socket.SendTo(buffer, SocketFlags.None, self.remoteAddress!);
        }
        return sent;
    }
}
