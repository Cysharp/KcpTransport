#pragma warning disable CS8500

using KcpTransport.LowLevel;
using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
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
    readonly long startingTimestamp = Stopwatch.GetTimestamp();

    // create by User, for client connection
    unsafe KcpConnection(Socket socket, uint conversationId)
    {
        this.kcp = ikcp_create(conversationId);
        this.kcp->output = &KcpOutputCallback;
        // this.kcp->writelog = KcpWriteLog;
        this.socket = socket;
        this.stream = new KcpStream(this);

        StartSocketEventLoop();
        UpdateTimestamp();
    }

    // create from Listerner for server connection
    internal unsafe KcpConnection(uint conversationId, IPEndPoint serverEndPoint, SocketAddress remoteAddress)
    {
        this.kcp = ikcp_create(conversationId);
        this.kcp->output = &KcpOutputCallback;
        // this.kcp->writelog = KcpWriteLog;
        this.remoteAddress = remoteAddress.Clone();

        // bind same port and connect client IP, this socket is used only for Send
        this.socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        this.socket.Bind(serverEndPoint);
        this.socket.Connect(remoteAddress.ToIPEndPoint());

        this.stream = new KcpStream(this);

        UpdateTimestamp();
    }

    public static async ValueTask<KcpConnection> ConnectAsync(IPEndPoint remoteAddress, CancellationToken cancellationToken = default)
    {
        var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        await socket.ConnectAsync(remoteAddress, cancellationToken);

        SendHandshakeRequest(socket);

        // TODO: retry?
        var initialWaitBuffer = new byte[4];
        var received = await socket.ReceiveAsync(initialWaitBuffer);
        if (received != 4) throw new Exception();

        var conversationId = MemoryMarshal.Read<uint>(initialWaitBuffer);

        var connection = new KcpConnection(socket, conversationId);
        return connection;

        static void SendHandshakeRequest(Socket socket)
        {
            Span<byte> data = stackalloc byte[4];
            MemoryMarshal.Write(data, (uint)PacketType.Handshake);
            socket.Send(data);
        }
    }

    async void StartSocketEventLoop()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

        // TODO: mtu size
        var socketBuffer = GC.AllocateUninitializedArray<byte>(1500, pinned: true); // Create to pinned object heap

        while (true)
        {
            // Socket is datagram so received contains full block
            var received = await socket.ReceiveAsync(socketBuffer, SocketFlags.None, CancellationToken.None);
            unsafe
            {
                var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));
                InputReceivedBuffer(socketBufferPointer, received);
            }

            if (ConsumeKcpFragments(null))
            {
                await stream.Writer.FlushAsync(CancellationToken.None);
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

    internal unsafe void SendReliableBuffer(ReadOnlySpan<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            ikcp_send(kcp, p, buffer.Length);
        }

        // TODO: auto flush?
        Flush();
    }

    internal unsafe void SendUnreliableBuffer(ReadOnlySpan<byte> buffer)
    {
        // 8 byte header.
        var packetLength = buffer.Length + 8;
        var rent = ArrayPool<byte>.Shared.Rent(packetLength);
        try
        {
            var span = rent.AsSpan();
            MemoryMarshal.Write(span, (uint)PacketType.Unreliable);
            MemoryMarshal.Write(span.Slice(4), kcp->conv);
            buffer.CopyTo(span.Slice(8));

            socket.Send(span.Slice(0, packetLength));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rent);
        }
    }

    internal unsafe void WriteRawBuffer(ReadOnlySpan<byte> span)
    {
        stream.Writer.Write(span);
    }

    internal unsafe void Flush()
    {
        ikcp_flush(kcp, this);
    }

    public unsafe void UpdateTimestamp()
    {
        var elapsed = Stopwatch.GetElapsedTime(startingTimestamp);
        KcpUpdateTimestamp((uint)elapsed.TotalMilliseconds);
    }

    internal unsafe void KcpUpdateTimestamp(uint currentTimestampMillisec)
    {
        ikcp_update(kcp, currentTimestampMillisec, this);
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
