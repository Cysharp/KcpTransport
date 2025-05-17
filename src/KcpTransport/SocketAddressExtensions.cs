using System.Net;

namespace KcpTransport
{
    internal static class SocketAddressExtensions
    {
        internal static IPEndPoint ToIPEndPoint(this SocketAddress socketAddress)
        {
            IPEndPoint endpoint = new IPEndPoint(0, 0);
            return (IPEndPoint)endpoint.Create(socketAddress);
        }

        internal static SocketAddress Clone(this SocketAddress socketAddress)
        {
            var clone = new SocketAddress(socketAddress.Family, socketAddress.Size);
#if NET8_0_OR_GREATER
            socketAddress.Buffer.CopyTo(clone.Buffer);
#else
            for (int i = 0; i < socketAddress.Size; i++)
            {
                clone[i] = socketAddress[i];
            }
#endif
            return clone;
        }
    }
}