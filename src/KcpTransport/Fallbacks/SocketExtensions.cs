using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpTransport.Fallbacks
{
    public static class SocketExtensions
    {
#if !NET6_0_OR_GREATER
        public static Task<SocketReceiveFromResult> ReceiveFromAsync(
            this Socket socket,
            ArraySegment<byte> buffer,
            SocketFlags socketFlags,
            EndPoint remoteEndPoint,
            CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<SocketReceiveFromResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var args = new SocketAsyncEventArgs { RemoteEndPoint = remoteEndPoint, SocketFlags = socketFlags };
            args.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

            args.Completed += OnCompleted;

            if (cancellationToken.IsCancellationRequested)
            {
                Cleanup(args);
                tcs.TrySetCanceled(cancellationToken);
                return tcs.Task;
            }

            var registration = cancellationToken.Register(() =>
            {
                Cleanup(args);
                tcs.TrySetCanceled(cancellationToken);
            });

            if (!socket.ReceiveFromAsync(args))
            {
                OnCompleted(socket, args);
            }

            return tcs.Task.ContinueWith(task =>
            {
                registration.Dispose();
                return task.GetAwaiter().GetResult();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            void Cleanup(SocketAsyncEventArgs eventArgs)
            {
                eventArgs.Completed -= OnCompleted;
                eventArgs.Dispose();
            }

            void OnCompleted(object? s, SocketAsyncEventArgs e)
            {
                Cleanup(args);
                if (e.SocketError == SocketError.Success)
                {
                    tcs.TrySetResult(new SocketReceiveFromResult { ReceivedBytes = e.BytesTransferred, RemoteEndPoint = e.RemoteEndPoint! });
                }
                else
                {
                    tcs.TrySetException(new SocketException((int)e.SocketError));
                }
            }
        }
#endif
    }
}
