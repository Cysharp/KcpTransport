using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

#nullable enable
namespace KcpTransport
{
    public class KcpSocket : IDisposable
    {
        private readonly Socket? _underlyingSocket;

        internal EndPoint? _remoteEndPoint;

        public KcpSocket(Socket underlyingSocket)
        {
            _underlyingSocket = underlyingSocket;
        }

        public void Bind(EndPoint localEndPoint)
        {
            _underlyingSocket?.Bind(localEndPoint);
        }

        public async ValueTask<uint> ConnectAsync(EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            _remoteEndPoint = remoteEndPoint;

            await _underlyingSocket!.ConnectAsync(remoteEndPoint, cancellationToken).ConfigureAwait(false);

            SendHandshakeInitialRequest(_underlyingSocket!);

            // TODO: retry?
            // TODO: retry?
            var handshakeBuffer = new byte[20];
            var received = await _underlyingSocket!.ReceiveAsync(handshakeBuffer, cancellationToken);
            if (received != 20) throw new Exception();

            var conversationId = MemoryMarshal.Read<uint>(handshakeBuffer.AsSpan(4));
            SendHandshakeOkRequest(_underlyingSocket!, handshakeBuffer);

            var received2 = await _underlyingSocket!.ReceiveAsync(handshakeBuffer, cancellationToken);
            if (received2 != 4) throw new Exception();
            var responseCode = (PacketType)MemoryMarshal.Read<uint>(handshakeBuffer);

            if (responseCode != PacketType.HandshakeOkResponse) throw new Exception();

            return conversationId;

            static void SendHandshakeInitialRequest(Socket socket)
            {
                Span<byte> data = stackalloc byte[4];
                MemoryMarshalPolyfill.Write(data, (uint)PacketType.HandshakeInitialRequest);
                socket?.Send(data);
            }

            static void SendHandshakeOkRequest(Socket socket, Span<byte> data)
            {
                MemoryMarshalPolyfill.Write(data, (uint)PacketType.HandshakeOkRequest);
                socket?.Send(data);
            }
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, CancellationToken cancellationToken = default) => _underlyingSocket!.ReceiveAsync(buffer, socketFlags, cancellationToken);

        public int Send(ReadOnlySpan<byte> buffer)
        {
#if NET8_0_OR_GREATER
            return _underlyingSocket!.SendTo(buffer, _remoteEndPoint!);
#else
            return _underlyingSocket!.SendTo(buffer.ToArray(), _remoteEndPoint);
#endif
        }

        public void Connect(EndPoint remoteEndPoint)
        {
            _remoteEndPoint = remoteEndPoint;
        }

        public void Dispose()
        {
            _underlyingSocket?.Dispose();
        }
    }
}
