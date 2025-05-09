﻿using System;
using System.Buffers;
using KcpTransport.LowLevel;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using KcpTransport.Fallbacks;

namespace KcpTransport
{
    public abstract
#if NET6_0_OR_GREATER
    record
#endif
        class KcpOptions
    {
        public bool EnableNoDelay { get; set; } = true;
        public int IntervalMilliseconds { get; set; } = 10; // ikcp_nodelay min is 10.
        public int Resend { get; set; } = 2;
        public bool EnableFlowControl { get; set; } = false;
        public (int SendWindow, int ReceiveWindow) WindowSize { get; set; } = ((int)KcpMethods.IKCP_WND_SND, (int)KcpMethods.IKCP_WND_RCV);

        public int MaximumTransmissionUnit { get; set; } = (int)KcpMethods.IKCP_MTU_DEF;
        // public int MinimumRetransmissionTimeout { get; set; } this value is changed in ikcp_nodelay(and use there default) so no configurable.
    }

    public enum ListenerSocketType
    {
        Receive,
        Send
    }

    public delegate ReadOnlySpan<byte> HashFunc();

    public sealed
#if NET6_0_OR_GREATER
    record
#endif
        class KcpListenerOptions : KcpOptions
    {
#if NET6_0_OR_GREATER
        static readonly byte[] DefaultRandomHashKey = RandomNumberGenerator.GetBytes(32);
#else
        private static readonly byte[] DefaultRandomHashKey;

        static KcpListenerOptions()
        {
            DefaultRandomHashKey = new byte[32];
            RandomNumberGenerator.Fill(DefaultRandomHashKey);
        }

#endif

        public
#if NET7_0_OR_GREATER
            required
#endif
            IPEndPoint ListenEndPoint { get; set; }

        public TimeSpan UpdatePeriod { get; set; } = TimeSpan.FromMilliseconds(5);
        public int EventLoopCount { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
        public bool ConfigureAwait { get; set; } = false;
        public TimeSpan KeepAliveDelay { get; set; } = TimeSpan.FromSeconds(20);
        public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan HandshakeTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public HashFunc Handshake32bitHashKeyGenerator { get; set; } = KeyGenerator;
        public Action<Socket, KcpListenerOptions, ListenerSocketType>? ConfigureSocket { get; set; }

        static ReadOnlySpan<byte> KeyGenerator() => DefaultRandomHashKey;
    }

    public sealed class KcpListener : IDisposable, IAsyncDisposable
    {
        Socket socket;
        Channel<KcpConnection> acceptQueue;
        bool isDisposed;
        ConcurrentDictionary<uint, KcpConnection> connections = new();

        Task[] socketEventLoopTasks;
        Thread updateConnectionsWorkerThread;
        CancellationTokenSource listenerCancellationTokenSource = new();

        public static ValueTask<KcpListener> ListenAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            return ListenAsync(new IPEndPoint(IPAddress.Parse(host), port), cancellationToken);
        }

        public static ValueTask<KcpListener> ListenAsync(IPEndPoint listenEndPoint, CancellationToken cancellationToken = default)
        {
            return ListenAsync(new KcpListenerOptions { ListenEndPoint = listenEndPoint }, cancellationToken);
        }

        public static ValueTask<KcpListener> ListenAsync(KcpListenerOptions options, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // sync but for future extensibility.
            return new ValueTask<KcpListener>(new KcpListener(options));
        }

        KcpListener(KcpListenerOptions options)
        {
            Socket socket = new Socket(options.ListenEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Blocking = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); // in Linux, as SO_REUSEPORT

            // http://handyresearcher.blog.fc2.com/blog-entry-18.html
            // https://stackoverflow.com/questions/7201862/an-existing-connection-was-forcibly-closed-by-the-remote-host/7478498
            // https://stackoverflow.com/questions/34242622/windows-udp-sockets-recvfrom-fails-with-error-10054
            // https://stackoverflow.com/questions/74327225/why-does-sending-via-a-udpclient-cause-subsequent-receiving-to-fail
            if (OperatingSystem.IsWindows())
            {
                const uint IOC_IN = 0x80000000U;
                const uint IOC_VENDOR = 0x18000000U;
                const uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                socket.IOControl(unchecked((int)SIO_UDP_CONNRESET), new byte[1] { 0x00 }, null);
            }

            options.ConfigureSocket?.Invoke(socket, options, ListenerSocketType.Receive);

            var endPoint = options.ListenEndPoint;
            try
            {
                socket.Bind(endPoint);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            this.socket = socket;
            this.acceptQueue = Channel.CreateUnbounded<KcpConnection>(new UnboundedChannelOptions() { SingleWriter = true });

            this.socketEventLoopTasks = new Task[options.EventLoopCount];
            for (int i = 0; i < socketEventLoopTasks.Length; i++)
            {
                socketEventLoopTasks[i] = this.StartSocketEventLoopAsync(options, i);
            }

            updateConnectionsWorkerThread = new Thread(RunUpdateKcpConnectionLoop) { Name = $"{nameof(KcpListener)}", IsBackground = true, Priority = ThreadPriority.AboveNormal, };
            updateConnectionsWorkerThread.Start(options);
        }

        public ValueTask<KcpConnection> AcceptConnectionAsync(CancellationToken cancellationToken = default)
        {
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(isDisposed, this);
#else
            if (isDisposed)
                throw new ObjectDisposedException(GetType().FullName);
#endif

            return acceptQueue.Reader.ReadAsync(cancellationToken);
        }

        async Task StartSocketEventLoopAsync(KcpListenerOptions options, int id)
        {
            // await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);

#if NET8_0_OR_GREATER
            var receivedAddress = new SocketAddress(options.ListenEndPoint.AddressFamily);
#else
            EndPoint receivedAddress = new IPEndPoint(options.ListenEndPoint.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any, 0);
#endif
            var random = new Random();
            var cancellationToken = this.listenerCancellationTokenSource.Token;

            var socketBuffer = GC.AllocateUninitializedArray<byte>(options.MaximumTransmissionUnit, pinned: true); // Create to pinned object heap

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Socket is datagram so received data contains full block
#if NET8_0_OR_GREATER
                    var received = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress, cancellationToken);
#else
                    var receivedResult = await socket.ReceiveFromAsync(socketBuffer, SocketFlags.None, receivedAddress, cancellationToken);
                    var received = receivedResult.ReceivedBytes;
                    receivedAddress = receivedResult.RemoteEndPoint;
#endif

                    // first 4 byte is conversationId or extra packet type
                    var conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(0, received));
                    var packetType = (PacketType)conversationId;
                    switch (packetType)
                    {
                        case PacketType.HandshakeInitialRequest:
                            ISSUE_CONVERSATION_ID:
                        {
                            conversationId = unchecked((uint)random.Next(100, int.MaxValue)); // 0~99 is reserved
                            if (connections.ContainsKey(conversationId))
                            {
                                goto ISSUE_CONVERSATION_ID;
                            }

                            // send tentative id and cookie to avoid syn-flood(don't allocate memory in this phase)
                            var (cookie, timestamp) = SynCookie.Generate(options.Handshake32bitHashKeyGenerator(), receivedAddress);

                            SendHandshakeInitialResponse(socket, receivedAddress, conversationId, cookie, timestamp);
                        }
                            break;
                        case PacketType.HandshakeOkRequest:
                            {
                                conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4, received - 4));
                                var cookie = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(8, received - 8));
                                var timestamp = MemoryMarshal.Read<long>(socketBuffer.AsSpan(12, received - 12));

                                if (!SynCookie.Validate(options.Handshake32bitHashKeyGenerator(), options.HandshakeTimeout, cookie, receivedAddress, timestamp))
                                {
                                    SendHandshakeNgResponse(socket, receivedAddress);
                                    break;
                                }

                                if (!connections.TryAdd(conversationId, null!))
                                {
                                    // can't added, client should retry
                                    SendHandshakeNgResponse(socket, receivedAddress);
                                    break;
                                }

                                // create new connection
#if NET8_0_OR_GREATER
                                var kcpConnection = new KcpConnection(conversationId, options, receivedAddress);
#else
                                var kcpConnection = new KcpConnection(conversationId, options, receivedAddress.Serialize());
#endif
                                connections[conversationId] = kcpConnection;
                                acceptQueue.Writer.TryWrite(kcpConnection);
                                SendHandshakeOkResponse(socket, receivedAddress);
                            }
                            break;
                        case PacketType.Ping:
                            {
                                conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4, received - 4));
                                if (!connections.TryGetValue(conversationId, out var kcpConnection))
                                {
                                    // may incoming old packet, TODO: log it.
                                    continue;
                                }

                                kcpConnection.PingReceived();
                            }
                            break;
                        case PacketType.Pong:
                            {
                                conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4, received - 4));
                                if (!connections.TryGetValue(conversationId, out var kcpConnection))
                                {
                                    // may incoming old packet, TODO: log it.
                                    continue;
                                }

                                kcpConnection.PongReceived();
                            }
                            break;
                        case PacketType.Disconnect:
                            {
                                conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4, received - 4));
                                if (!connections.TryRemove(conversationId, out var kcpConnection))
                                {
                                    // may incoming old packet, TODO: log it.
                                    continue;
                                }

                                kcpConnection.Disconnect();
                                kcpConnection.Dispose();
                            }
                            break;
                        case PacketType.Unreliable:
                            {
                                conversationId = MemoryMarshal.Read<uint>(socketBuffer.AsSpan(4, received - 4));
                                if (!connections.TryGetValue(conversationId, out var kcpConnection))
                                {
                                    // may incoming old packet, TODO: log it.
                                    continue;
                                }


                                // This loop is sometimes called in multithread so needs lock per connection.
                                await kcpConnection.AwaitForLastFlushResult();
                                lock (kcpConnection.SyncRoot)
                                {
                                    kcpConnection.InputReceivedUnreliableBuffer(socketBuffer.AsSpan(8, received - 8));
                                    kcpConnection.FlushReceivedBuffer(cancellationToken);
                                }
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

                                if (!connections.TryGetValue(conversationId, out var kcpConnection))
                                {
                                    // may incoming old packet, TODO: log it.
                                    continue;
                                }

                                await kcpConnection.AwaitForLastFlushResult();
                                lock (kcpConnection.SyncRoot)
                                {
                                    unsafe
                                    {
                                        var socketBufferPointer = (byte*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(socketBuffer));
                                        if (!kcpConnection.InputReceivedKcpBuffer(socketBufferPointer, received)) continue;
                                    }

#if NET8_0_OR_GREATER
                                    kcpConnection.ConsumeKcpFragments(receivedAddress, cancellationToken);
#else
                                    kcpConnection.ConsumeKcpFragments(receivedAddress.Serialize(), cancellationToken);
#endif
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    // TODO: log?
                    _ = ex;
                }
            }

            static void SendHandshakeInitialResponse(Socket socket,
#if NET8_0_OR_GREATER
                SocketAddress
#else
                EndPoint
#endif
                    clientAddress, uint conversationId, uint cookie, long timestamp)
            {
#if NET6_0_OR_GREATER
                Span<byte> data = stackalloc byte[20]; // type(4) + conv(4) + cookie(4) + timestamp(8)
#else
                byte[] array = ArrayPool<byte>.Shared.Rent(20);
                Span<byte> data = array.AsSpan(0, 20);
#endif
                MemoryMarshalFallback.Write(data, (uint)PacketType.HandshakeInitialResponse);
                MemoryMarshalFallback.Write(data.Slice(4), conversationId);
                MemoryMarshalFallback.Write(data.Slice(8), cookie);
                MemoryMarshalFallback.Write(data.Slice(12), timestamp);
#if NET6_0_OR_GREATER
                socket.SendTo(data, SocketFlags.None, clientAddress);
#else
                socket.SendTo(array, 0, 20, SocketFlags.None, clientAddress);
#endif
            }

            static void SendHandshakeOkResponse(Socket socket,
#if NET8_0_OR_GREATER
                SocketAddress
#else
                EndPoint
#endif
                    clientAddress)
            {
#if NET6_0_OR_GREATER
                Span<byte> data = stackalloc byte[4];
#else
                byte[] array = ArrayPool<byte>.Shared.Rent(4);
                Span<byte> data = array.AsSpan(0, 4);
#endif
                MemoryMarshalFallback.Write(data, (uint)PacketType.HandshakeOkResponse);
#if NET6_0_OR_GREATER
                socket.SendTo(data, SocketFlags.None, clientAddress);
#else
                socket.SendTo(array, 0, 4, SocketFlags.None, clientAddress);
#endif
            }

            static void SendHandshakeNgResponse(Socket socket,
#if NET8_0_OR_GREATER
                SocketAddress
#else
                EndPoint
#endif
                    clientAddress)
            {
#if NET6_0_OR_GREATER
                Span<byte> data = stackalloc byte[4];
#else
                byte[] array = ArrayPool<byte>.Shared.Rent(4);
                Span<byte> data = array.AsSpan(0, 4);
#endif
                MemoryMarshalFallback.Write(data, (uint)PacketType.HandshakeNgResponse);
#if NET6_0_OR_GREATER
                socket.SendTo(data, SocketFlags.None, clientAddress);
#else
                socket.SendTo(array, 0, 4, SocketFlags.None, clientAddress);
#endif
            }
        }

        void RunUpdateKcpConnectionLoop(object? state)
        {
            // NOTE: should use ikcp_check? https://github.com/skywind3000/kcp/wiki/EN_KCP-Best-Practice#advance-update

            // All Windows(.NET) Timer and Sleep is low-resolution(min is 16ms).
            // We use custom high-resolution timer instead.

            var cancellationToken = listenerCancellationTokenSource.Token;
            var options = (KcpListenerOptions)state!;
            var period = options.UpdatePeriod;
            var timeout = options.ConnectionTimeout;
            var waitTime = (int)period.TotalMilliseconds;

            var removeConnection = new List<uint>();
            while (true)
            {
                SleepInterop.Sleep(waitTime);

                if (cancellationToken.IsCancellationRequested) break;

                var currentTimestamp = Stopwatch.GetTimestamp();
                foreach (var kvp in connections)
                {
                    var connection = kvp.Value;
                    if (connection != null) // at handshake, connection is not created yet so sometimes null...
                    {
                        if (connection.IsAlive(currentTimestamp, timeout))
                        {
                            connection.TrySendPing(currentTimestamp);
                            connection.UpdateKcp();
                        }
                        else
                        {
                            removeConnection.Add(kvp.Key);
                        }
                    }
                }

                if (removeConnection.Count > 0)
                {
                    foreach (var id in removeConnection)
                    {
                        if (connections.TryRemove(id, out var conn))
                        {
                            conn.Disconnect();
                            conn.Dispose();
                        }
                    }

                    removeConnection.Clear();
                }
            }
        }

        public unsafe void Dispose()
        {
            DisposeAsync().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (isDisposed) return;
            isDisposed = true;

            acceptQueue.Writer.Complete();

            // loop will stop
            listenerCancellationTokenSource.Cancel();
            listenerCancellationTokenSource.Dispose();

            // wait loop complete
            try
            {
                var curr = SynchronizationContext.Current;
                await Task.WhenAll(socketEventLoopTasks);
            }
            catch
            {
            }

            // before socket dispose, send disocnnected event to all connections
            foreach (var connection in connections)
            {
                var conn = connection.Value;
                if (conn != null)
                {
                    conn.Disconnect();
                    conn.Dispose();
                }
            }

            connections.Clear();

            socket.Dispose();
        }
    }
}
