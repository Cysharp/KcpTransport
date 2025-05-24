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
            cancellationToken.ThrowIfCancellationRequested();

            var registration = cancellationToken.Register(socket.Close);

            try
            {
                var result = await socket.ReceiveFromAsync(buffer, socketFlags, remoteEndPoint).ConfigureAwait(false);
                return result;
            }
            finally
            {
                registration.Dispose();
            }
        }
#endif

#if !NET5_0_OR_GREATER
        public static async ValueTask ConnectAsync(this Socket socket, EndPoint endPoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var registration = cancellationToken.Register(socket.Close);

            try
            {
                await socket.ConnectAsync(endPoint).ConfigureAwait(false);
            }
            finally
            {
                registration.Dispose();
            }
        }
#endif

    }
}
