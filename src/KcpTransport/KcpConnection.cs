﻿#pragma warning disable CS8500

using KcpTransport.LowLevel;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using static KcpTransport.LowLevel.KcpMethods;

namespace KcpTransport
{

#if LANG_VERSION_10_OR_GREATER
    public sealed record class KcpClientConnectionOptions : KcpOptions
#else
    public sealed class KcpClientConnectionOptions : KcpOptions
#endif

    {
#if LANG_VERSION_11_OR_GREATER
        public required EndPoint RemoteEndPoint { get; set; }
#else
        public EndPoint? RemoteEndPoint { get; set; }
#endif
        public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromMilliseconds(5);
        public bool ConfigureAwait { get; set; } = false;
        public TimeSpan KeepAliveDelay { get; set; } = TimeSpan.FromSeconds(20);
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(1);
        public Action<Socket, KcpClientConnectionOptions>? ConfigureSocket { get; set; }
    }

    public sealed class KcpDisconnectedException : Exception
    {

    }

    // KcpConnections is both used for server and client
    public class KcpConnection : IDisposable
    {
        static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(5);
        public EndPoint? RemoteEndPoint { get => remoteAddress?.ToIPEndPoint(); }
        unsafe IKCPCB* kcp;
        uint conversationId;
        SocketAddress? remoteAddress;
        KcpStream stream;
        KcpSocket socket;
        Task? receiveEventLoopTask; // only used for client
        Thread? updateKcpWorkerThread; // only used for client
        ValueTask<FlushResult> lastFlushResult = default;

        readonly long startingTimestamp = Stopwatch.GetTimestamp();
        readonly TimeSpan keepAliveDelay;
        CancellationTokenSource connectionCancellationTokenSource = new();
        readonly object gate = new object();
        long lastReceivedTimestamp;
        long lastPingSent;
        bool isDisposed;

        public uint ConnectionId => conversationId;
        internal object SyncRoot => gate;

        // create by User(from KcpConnection.ConnectAsync), for client connection
        unsafe KcpConnection(KcpSocket socket, uint conversationId, KcpClientConnectionOptions options)
        {
            this.conversationId = conversationId;
            this.keepAliveDelay = options.KeepAliveDelay;
            this.kcp = ikcp_create(conversationId, GCHandle.ToIntPtr(GCHandle.Alloc(this)).ToPointer());
            this.kcp->output = &KcpOutputCallback;
            this.kcp->stream = options.SteamMode;
            ConfigKcpWorkMode(options.EnableNoDelay, options.IntervalMilliseconds, options.Resend, options.EnableFlowControl);
            ConfigKcpWindowSize(options.WindowSize.SendWindow, options.WindowSize.ReceiveWindow);
            ConfigKcpMaximumTransmissionUnit(options.MaximumTransmissionUnit);
            this.remoteAddress = options?.RemoteEndPoint?.Serialize();
            this.socket = socket;
            this.stream = new KcpStream(this);
            this.lastReceivedTimestamp = startingTimestamp;

            this.receiveEventLoopTask = StartSocketEventLoopAsync(options);

            UpdateKcp(); // initial set kcp timestamp
            updateKcpWorkerThread = new Thread(RunUpdateKcpLoop)
            {
                Name = $"{nameof(KcpConnection)}-{conversationId}",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };
            updateKcpWorkerThread.Start(options);
        }

        // create from Listerner for server connection
        internal unsafe KcpConnection(uint conversationId, KcpListenerOptions options, SocketAddress remoteAddress)
        {
            this.conversationId = conversationId;
            this.keepAliveDelay = options.KeepAliveDelay;
            this.kcp = ikcp_create(conversationId, GCHandle.ToIntPtr(GCHandle.Alloc(this)).ToPointer());
            this.kcp->stream = options.SteamMode;
            this.kcp->output = &KcpOutputCallback;
            ConfigKcpWorkMode(options.EnableNoDelay, options.IntervalMilliseconds, options.Resend, options.EnableFlowControl);
            ConfigKcpWindowSize(options.WindowSize.SendWindow, options.WindowSize.ReceiveWindow);
            ConfigKcpMaximumTransmissionUnit(options.MaximumTransmissionUnit);

            this.remoteAddress = remoteAddress.Clone();

            // bind same port and connect client IP, this socket is used only for Send

            var socket = new Socket(remoteAddress.Family, SocketType.Dgram, ProtocolType.Udp);

            socket.Blocking = false;

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            this.socket = new KcpSocket(socket);

            options.ConfigureSocket?.Invoke(socket, options, ListenerSocketType.Send);

            this.socket.Bind(options.ListenEndPoint);

            this.socket.Connect(remoteAddress.ToIPEndPoint());

            this.stream = new KcpStream(this);
            this.lastReceivedTimestamp = startingTimestamp;

            UpdateKcp(); // initial set kcp timestamp
                         // StartUpdateKcpLoopAsync(); server operation, Update will be called from KcpListener so no need update self.

        }

        public static ValueTask<KcpConnection> ConnectAsync(string host, int port, int streamMode = 0, CancellationToken cancellationToken = default)
        {
            return ConnectAsync(new IPEndPoint(IPAddress.Parse(host), port), streamMode, cancellationToken);
        }

        public static ValueTask<KcpConnection> ConnectAsync(EndPoint remoteEndPoint, int streamMode = 0, CancellationToken cancellationToken = default)
        {
            return ConnectAsync(new KcpClientConnectionOptions { RemoteEndPoint = remoteEndPoint, SteamMode = streamMode }, cancellationToken);
        }

        public static async ValueTask<KcpConnection> ConnectAsync(KcpClientConnectionOptions options, CancellationToken cancellationToken = default)
        {
            var socket = new Socket(options.RemoteEndPoint?.AddressFamily ?? AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

            socket.Blocking = false;

            options.ConfigureSocket?.Invoke(socket, options);

            var kcpSocket = new KcpSocket(socket);

            var conversationId = await kcpSocket.ConnectAsync(options.RemoteEndPoint!, cancellationToken).ConfigureAwait(options.ConfigureAwait);

            var connection = new KcpConnection(kcpSocket, conversationId, options);

            return connection;
        }

        public ValueTask<KcpStream> OpenOutboundStreamAsync()
        {
            return new ValueTask<KcpStream>(stream);
        }

        async Task StartSocketEventLoopAsync(KcpClientConnectionOptions options)
        {
#if NET8_0_OR_GREATER
            var forceYielding = ConfigureAwaitOptions.ForceYielding;
#else
            var forceYielding = false;
            await Task.Yield();
#endif

            await Task.CompletedTask.ConfigureAwait(forceYielding);

            var cancellationToken = this.connectionCancellationTokenSource.Token;

            var socketBuffer = GCPolyfill.AllocateUninitializedArray<byte>(options.MaximumTransmissionUnit, pinned: true); // Create to pinned object heap

            while (true)
            {
                if (isDisposed) return;

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
                    case PacketType.Ping:
                        PingReceived();
                        break;
                    case PacketType.Pong:
                        PongReceived();
                        break;
                    case PacketType.Disconnect:
                        Disconnect();
                        Dispose();
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
                                var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshalPolyfill.GetArrayDataReference(socketBuffer));
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
            var options = (KcpClientConnectionOptions)state!;
            var period = options.UpdatePeriod;
            var timeout = options.ConnectionTimeout;
            var waitTime = (int)period.TotalMilliseconds;

            while (true)
            {
                SleepInterop.Sleep(waitTime);

                if (cancellationToken.IsCancellationRequested) break;

                var currentTimestamp = Stopwatch.GetTimestamp();
                if (IsAlive(currentTimestamp, timeout))
                {
                    TrySendPing(currentTimestamp);
                    UpdateKcp();
                }
                else
                {
                    // TODO: Disconnect.
                }
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
            lastReceivedTimestamp = Stopwatch.GetTimestamp();
            lock (gate)
            {
                if (isDisposed) return false;

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
                if (isDisposed) return;

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
            lastReceivedTimestamp = Stopwatch.GetTimestamp();
            stream.InputWriter.Write(span);
        }

        // KcpStream.Write operations send buffer to socket.

        internal unsafe void SendReliableBuffer(ReadOnlySpan<byte> buffer)
        {
            fixed (byte* p = buffer)
            {
                lock (gate)
                {
                    if (isDisposed) return;

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
                MemoryMarshalPolyfill.Write(span, (uint)PacketType.Unreliable);
                MemoryMarshalPolyfill.Write(span.Slice(4), kcp->conv);
                buffer.CopyTo(span.Slice(8));

                lock (gate)
                {
                    if (isDisposed) return;

                    socket.Send(span.Slice(0, packetLength));
                }
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
                if (isDisposed) return;

                ikcp_flush(kcp);
            }
        }

        internal unsafe void UpdateKcp()
        {
            var elapsed = StopwatchPolyfiil.GetElapsedTime(startingTimestamp);
            var currentTimestampMilliseconds = (uint)elapsed.TotalMilliseconds;
            lock (gate)
            {
                if (isDisposed) return;

                ikcp_update(kcp, currentTimestampMilliseconds);
            }
        }

        internal unsafe bool IsAlive(long currentTimestamp, TimeSpan timeout)
        {
            if (isDisposed) return false;

            // ikcp.c
            // if (segment->xmit >= kcp->dead_link) kcp->state = unchecked((IUINT32)(-1));
            if (kcp->state == unchecked((uint)(-1)))
            {
                return false;
            }

            var elapsed = StopwatchPolyfiil.GetElapsedTime(lastReceivedTimestamp, currentTimestamp);
            if (elapsed < timeout)
            {
                return true;
            }

            return false;
        }

        internal unsafe void TrySendPing(long currentTimestamp)
        {
            if (isDisposed) return;

            var elapsed = StopwatchPolyfiil.GetElapsedTime(lastReceivedTimestamp, currentTimestamp);
            if (elapsed > keepAliveDelay)
            {
                // send ping per 5 seconds.
                var ping = StopwatchPolyfiil.GetElapsedTime(lastPingSent, currentTimestamp);
                if (ping > PingInterval)
                {
                    lastPingSent = currentTimestamp;
                    Span<byte> pingBuffer = stackalloc byte[8];
                    MemoryMarshalPolyfill.Write(pingBuffer, (uint)PacketType.Ping);
                    MemoryMarshalPolyfill.Write(pingBuffer.Slice(4), conversationId);
                    lock (gate)
                    {
                        if (isDisposed) return;

                        socket.Send(pingBuffer);
                    }
                }
            }
        }

        internal unsafe void PingReceived()
        {
            lastReceivedTimestamp = Stopwatch.GetTimestamp();

            // send Pong
            Span<byte> pongBuffer = stackalloc byte[8];
            MemoryMarshalPolyfill.Write(pongBuffer, (uint)PacketType.Pong);
            MemoryMarshalPolyfill.Write(pongBuffer.Slice(4), conversationId);

            lock (gate)
            {
                if (isDisposed) return;

                socket.Send(pongBuffer);
            }
        }

        internal unsafe void PongReceived()
        {
            lastReceivedTimestamp = Stopwatch.GetTimestamp();
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
                if (isDisposed) return;

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
                if (isDisposed) return;

                ikcp_wndsize(kcp, sendWindow, receiveWindow);
            }
        }

        unsafe void ConfigKcpMaximumTransmissionUnit(int mtu)
        {
            // The MTU(Maximum Transmission Unit) default to 1400 bytes, which can be set by ikcp_setmtu.
            // Notice that KCP never probe the MTU, user must tell KCP the right MTU to use if need.
            lock (gate)
            {
                if (isDisposed) return;

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
                if (isDisposed) return;

                kcp->rx_minrto = minimumRto;
            }
        }

        public unsafe void Disconnect()
        {
            if (isDisposed) return;

            // Send disconnect message
            Span<byte> message = stackalloc byte[8];
            MemoryMarshalPolyfill.Write(message, (uint)PacketType.Disconnect);
            MemoryMarshalPolyfill.Write(message.Slice(4), conversationId);
            lock (gate)
            {
                socket.Send(message);
            }

            stream.InputWriter.Complete(new KcpDisconnectedException());
        }

        public unsafe void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        unsafe void Dispose(bool disposing)
        {
            Disconnect();

            lock (gate)
            {
                if (isDisposed) return;
                isDisposed = true;

                if (disposing)
                {
                    connectionCancellationTokenSource.Cancel();
                    connectionCancellationTokenSource.Dispose();

                    GCHandle.FromIntPtr((nint)kcp->user).Free();
                    ikcp_release(kcp);
                    kcp = null;

                    socket.Dispose();
                    socket = null!;

                    stream.Dispose();
                }
                else
                {
                    // only cleanup unmanaged resource
                    GCHandle.FromIntPtr((nint)kcp->user).Free();
                    ikcp_release(kcp);
                }
            }
        }

        ~KcpConnection()
        {
            Dispose(false);
        }


        static unsafe int KcpOutputCallback(byte* buf, int len, IKCPCB* kcp, void* user)
        {
            var self = (KcpConnection)GCHandle.FromIntPtr((IntPtr)user).Target!;
            var buffer = new Span<byte>(buf, len);

            var sent = self.socket.Send(buffer);
            return sent;
        }

        //static unsafe void KcpWriteLog(string msg, IKCPCB* kcp, object user)
        //{
        //    Console.WriteLine(msg);
        //}
    }
}
