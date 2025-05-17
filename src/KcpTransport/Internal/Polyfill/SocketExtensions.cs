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

        public static ValueTask<SocketReceiveFromResult> ReceiveFromAsync(this Socket socket, ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {

            cancellationToken.ThrowIfCancellationRequested();

            var task = Task.Run(async () =>
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEndPoint).ConfigureAwait(false);
                return result;
            }, cancellationToken);

            return new ValueTask<SocketReceiveFromResult>(task);
        }
#endif

#if !NET5_0_OR_GREATER
        public static ValueTask ConnectAsync(this Socket socket, EndPoint endPoint, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var task = Task.Run(async () =>
           {
               await socket.ConnectAsync(endPoint).ConfigureAwait(false);
           }, cancellationToken);

            return new ValueTask(task);
        }
#endif
    }
}
