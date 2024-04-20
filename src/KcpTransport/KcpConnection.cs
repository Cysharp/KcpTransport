﻿#pragma warning disable CS8500

using KcpTransport.LowLevel;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static KcpTransport.LowLevel.KcpMethods;

namespace KcpTransport;

public sealed record class KcpClientConnectionOptions : KcpOptions
{
    public required EndPoint RemoteEndPoint { get; set; }
    public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromMilliseconds(5);
    public bool ConfigureAwait { get; set; } = false;
}

// KcpConnections is both used for server and client
public class KcpConnection : IDisposable
{
    unsafe IKCPCB* kcp;
    SocketAddress? remoteAddress;
    KcpStream stream;
    Socket socket;
    Task? receiveEventLoopTask; // only used for client
    Thread? updateKcpWorkerThread; // only used for client
    ValueTask<FlushResult> lastFlushResult = default;

    readonly long startingTimestamp = Stopwatch.GetTimestamp();
    CancellationTokenSource connectionCancellationTokenSource = new();
    readonly object gate = new object();

    // create by User(from KcpConnection.ConnectAsync), for client connection
    unsafe KcpConnection(Socket socket, uint conversationId, KcpClientConnectionOptions options)
    {
        this.kcp = ikcp_create(conversationId);
        this.kcp->output = &KcpOutputCallback;
        ConfigKcpWorkMode(options.EnableNoDelay, options.IntervalMilliseconds, options.Resend, options.EnableFlowControl);
        ConfigKcpWindowSize(options.WindowSize.SendWindow, options.WindowSize.ReceiveWindow);
        ConfigKcpMaximumTransmissionUnit(options.MaximumTransmissionUnit);

        this.socket = socket;
        this.stream = new KcpStream(this);

        this.receiveEventLoopTask = StartSocketEventLoopAsync();

        UpdateKcp(); // initial set kcp timestamp
        updateKcpWorkerThread = new Thread(RunUpdateKcpLoop)
        {
            Name = $"{nameof(KcpConnection)}-{conversationId}",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
        };
        updateKcpWorkerThread.Start(options.UpdatePeriod);
    }

    // create from Listerner for server connection
    internal unsafe KcpConnection(uint conversationId, KcpListenerOptions options, SocketAddress remoteAddress)
    {
        this.kcp = ikcp_create(conversationId);
        this.kcp->output = &KcpOutputCallback;
        ConfigKcpWorkMode(options.EnableNoDelay, options.IntervalMilliseconds, options.Resend, options.EnableFlowControl);
        ConfigKcpWindowSize(options.WindowSize.SendWindow, options.WindowSize.ReceiveWindow);
        ConfigKcpMaximumTransmissionUnit(options.MaximumTransmissionUnit);

        this.remoteAddress = remoteAddress.Clone();

        // bind same port and connect client IP, this socket is used only for Send
        this.socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        this.socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        this.socket.Bind(options.ListenEndPoint);
        this.socket.Connect(remoteAddress.ToIPEndPoint());

        this.stream = new KcpStream(this);

        UpdateKcp(); // initial set kcp timestamp
        // StartUpdateKcpLoopAsync(); server operation, Update will be called from KcpListener so no need update self.
    }

    public static ValueTask<KcpConnection> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        return ConnectAsync(new IPEndPoint(IPAddress.Parse(host), port), cancellationToken);
    }

    public static ValueTask<KcpConnection> ConnectAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken = default)
    {
        return ConnectAsync(new KcpClientConnectionOptions { RemoteEndPoint = remoteEndPoint }, cancellationToken);
    }

    public static async ValueTask<KcpConnection> ConnectAsync(KcpClientConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var socket = new Socket(SocketType.Dgram, ProtocolType.Udp);
        await socket.ConnectAsync(options.RemoteEndPoint, cancellationToken).ConfigureAwait(options.ConfigureAwait);

        SendHandshakeRequest(socket);

        // TODO: retry?
        var initialWaitBuffer = new byte[4];
        var received = await socket.ReceiveAsync(initialWaitBuffer);
        if (received != 4) throw new Exception();

        var conversationId = MemoryMarshal.Read<uint>(initialWaitBuffer);

        var connection = new KcpConnection(socket, conversationId, options);
        return connection;

        static void SendHandshakeRequest(Socket socket)
        {
            Span<byte> data = stackalloc byte[4];
            MemoryMarshal.Write(data, (uint)PacketType.Handshake);
            socket.Send(data);
        }
    }

    public ValueTask<KcpStream> OpenOutboundStreamAsync()
    {
        return new ValueTask<KcpStream>(stream);
    }

    async Task StartSocketEventLoopAsync()
    {
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        var cancellationToken = this.connectionCancellationTokenSource.Token;

        // TODO: mtu size
        var socketBuffer = GC.AllocateUninitializedArray<byte>(1500, pinned: true); // Create to pinned object heap

        while (true)
        {
            // Socket is datagram so received contains full block
            var received = await socket.ReceiveAsync(socketBuffer, SocketFlags.None, cancellationToken);

            var conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(0, received));
            var packetType = (PacketType)conversationId;
            switch (packetType)
            {
                case PacketType.Unreliable:
                    {
                        conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4, received - 4));
                        InputReceivedUnreliableBuffer(socketBuffer.AsSpan(8, received - 8));
                        await stream.InputWriter.FlushAsync(cancellationToken);
                    }
                    break;
                default:
                    {
                        // Reliable
                        if (conversationId < 100)
                        {
                            // may incoming invalid packet, TODO: log it.
                            continue;
                        }

                        unsafe
                        {
                            var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));
                            if (!InputReceivedKcpBuffer(socketBufferPointer, received)) continue;
                        }

                        await AwaitForLastFlushResult();
                        ConsumeKcpFragments(null, cancellationToken);
                    }
                    break;
            }
        }
    }

    // same of KcpListener.RunUpdateKcpConnectionLoop
    void RunUpdateKcpLoop(object? state)
    {
        var cancellationToken = connectionCancellationTokenSource.Token;
        var period = (TimeSpan)state!;
        var waitTime = (int)period.TotalMilliseconds;

        while (true)
        {
            SleepInterop.Sleep(waitTime);

            if (cancellationToken.IsCancellationRequested) break;

            UpdateKcp();
        }
    }

    internal ValueTask<FlushResult> AwaitForLastFlushResult() => lastFlushResult;

    // write decoded kcp buffer to PipeStream(call after ConsumeKcpFragments successfully)
    // need to call AwaitForLastFlushResult before call this.
    internal void FlushReceivedBuffer(CancellationToken cancellationToken)
    {
        lastFlushResult = stream.InputWriter.FlushAsync(cancellationToken);
    }

    internal unsafe bool InputReceivedKcpBuffer(byte* buffer, int length)
    {
        lock (gate)
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
    }

    internal unsafe void ConsumeKcpFragments(SocketAddress? remoteAddress, CancellationToken cancellationToken)
    {
        var needFlush = false;
        lock (gate)
        {
            var size = ikcp_peeksize(kcp);
            if (size > 0)
            {
                if (remoteAddress != null && !remoteAddress.Equals(this.remoteAddress))
                {
                    // TODO: shutdown existing socket and create new one?
                }

                var writer = stream.InputWriter;
                var buffer = writer.GetSpan(size);

                fixed (byte* p = buffer)
                {
                    var len = ikcp_recv(kcp, p, buffer.Length);
                    if (len > 0)
                    {
                        writer.Advance(len);
                        needFlush = true;
                    }
                }
            }
        }

        if (needFlush)
        {
            FlushReceivedBuffer(cancellationToken);
        }
    }

    internal unsafe void InputReceivedUnreliableBuffer(ReadOnlySpan<byte> span)
    {
        stream.InputWriter.Write(span);
    }

    // KcpStream.Write operations send buffer to socket.

    internal unsafe void SendReliableBuffer(ReadOnlySpan<byte> buffer)
    {
        fixed (byte* p = buffer)
        {
            lock (gate)
            {
                ikcp_send(kcp, p, buffer.Length);
            }
        }
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

    internal unsafe void KcpFlush()
    {
        lock (gate)
        {
            ikcp_flush(kcp, this);
        }
    }

    internal unsafe void UpdateKcp()
    {
        var elapsed = Stopwatch.GetElapsedTime(startingTimestamp);
        var currentTimestampMillisec = (uint)elapsed.TotalMilliseconds;
        lock (gate)
        {
            ikcp_update(kcp, currentTimestampMillisec, this);
        }
    }

    // https://github.com/skywind3000/kcp/wiki/EN_KCP-Basic-Usage#config-kcp

    unsafe void ConfigKcpWorkMode(bool enableNoDelay, int intervalMilliseconds, int resend, bool enableFlowControl)
    {
        // int ikcp_nodelay(ikcpcb *kcp, int nodelay, int interval, int resend, int nc)
        // nodelay: Whether enable nodelay mode. 0: Off; 1: On.
        // interval: The internal interval in milliseconds, such as 10ms or 30ms.
        // resend: Whether enable fast retransmit mode. 0: Off; 2: Retransmit when missed in 2 ACK.
        // nc: Whether disable the flow control. 0: Enable. 1: Disable.

        // For normal mode, like TCP: ikcp_nodelay(kcp, 0, 40, 0, 0);
        // For high efficient transport: ikcp_nodelay(kcp, 1, 10, 2, 1);

        lock (gate)
        {
            ikcp_nodelay(kcp, enableNoDelay ? 1 : 0, intervalMilliseconds, resend, enableFlowControl ? 0 : 1);
        }
    }

    unsafe void ConfigKcpWindowSize(int sendWindow, int receiveWindow)
    {
        // int ikcp_wndsize(ikcpcb* kcp, int sndwnd, int rcvwnd);
        // Setup the max send or receive window size in packets,
        // default to 32 packets.Similar to TCP SO_SNDBUF and SO_RECVBUF, but they are in bytes, while ikcp_wndsize in packets.
        lock (gate)
        {
            ikcp_wndsize(kcp, sendWindow, receiveWindow);
        }
    }

    unsafe void ConfigKcpMaximumTransmissionUnit(int mtu)
    {
        // The MTU(Maximum Transmission Unit) default to 1400 bytes, which can be set by ikcp_setmtu.
        // Notice that KCP never probe the MTU, user must tell KCP the right MTU to use if need.
        lock (gate)
        {
            if (kcp->mtu != mtu)
            {
                ikcp_setmtu(kcp, mtu);
            }
        }
    }

    unsafe void ConfigKcpMinimumRetransmissionTimeout(int minimumRto)
    {
        // Both TCP and KCP use minimum RTO, for example, when calculated RTO is 40ms but default minimum RTO is 100ms,
        // then KCP never detect the dropped packet util after 100ms.The default value of minimum RTO is 100ms for normal mode,
        // while 30ms for high efficient transport.
        // User can setup minimum RTO by: kcp->rx_minrto = 10;
        lock (gate)
        {
            kcp->rx_minrto = minimumRto;
        }
    }

    public unsafe void Dispose()
    {
        // TODO: isDisposed.
        lock (gate)
        {
            ikcp_release(kcp);
        }
    }

    static unsafe int KcpOutputCallback(byte* buf, int len, IKCPCB* kcp, object user)
    {
        var self = (KcpConnection)user;
        var buffer = new Span<byte>(buf, len);

        var sent = self.socket.Send(buffer);
        return sent;
    }

    //static unsafe void KcpWriteLog(string msg, IKCPCB* kcp, object user)
    //{
    //    Console.WriteLine(msg);
    //}
}
