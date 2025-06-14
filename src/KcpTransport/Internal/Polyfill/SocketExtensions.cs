using System;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KcpTransport
{
    internal static class SocketExtensions
    {
#if !NET6_0_OR_GREATER

        public static async ValueTask<SocketReceiveFromResult> ReceiveFromAsync(this Socket socket, ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            return await socket.ReceiveFromAsync(buffer, socketFlags, remoteEndPoint).WithCancellation(cancellationToken).ConfigureAwait(false);
        }

        public static async ValueTask<int> ReceiveAsync(this Socket socket, Memory<byte> buffer, CancellationToken cancellationToken = default) {
            return await socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
        }
#endif

#if !NET5_0_OR_GREATER
        public static async ValueTask ConnectAsync(this Socket socket, EndPoint endPoint, CancellationToken cancellationToken)
        {
            await socket.ConnectAsync(endPoint).WithCancellation(cancellationToken).ConfigureAwait(false);
        }
#endif

    }
}
