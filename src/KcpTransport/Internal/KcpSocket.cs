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
    public class KcpSocket : Socket
    {
        ListenerSocketType? _listenerSocketType;

        internal EndPoint? _remoteEndPoint;

        public new EndPoint? RemoteEndPoint { get => _remoteEndPoint; set => _remoteEndPoint = value; }

        public ListenerSocketType? @ListenerSocketType { get => _listenerSocketType; set => _listenerSocketType = value; }

        public KcpSocket(AddressFamily addressFamily, SocketType socketType, ProtocolType protocolType) : base(addressFamily, socketType, protocolType)
        {
        }

        public void Bind(EndPoint localEP)
        {
            if (_listenerSocketType == KcpTransport.ListenerSocketType.Receive)
            {
                base.Bind(localEP);
            }
        }

        public new ValueTask ConnectAsync(EndPoint remoteEP, CancellationToken cancellationToken)
        {
            _remoteEndPoint = remoteEP;
#if NET5_0_OR_GREATER
            return ValueTask.CompletedTask;
#else
            return default;
#endif
        }

        public new int Send(ReadOnlySpan<byte> buffer)
        {
#if NET8_0_OR_GREATER
            return base.SendTo(buffer, RemoteEndPoint);
#else
            return base.SendTo(buffer.ToArray(), RemoteEndPoint);
#endif
        }

        public new void Connect(EndPoint remoteEP)
        {
            if (_listenerSocketType ==  KcpTransport.ListenerSocketType.Send)
            {
                this.RemoteEndPoint = remoteEP;
            }
        }
    }
}
