using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KcpTransport.Fallbacks
{
    public static class SocketExtensions
    {
#if !NET6_0_OR_GREATER
        public static ValueTask<SocketReceiveFromResult> ReceiveFromAsync(this Socket socket, ArraySegment<byte> buffer, SocketFlags socketFlags, EndPoint remoteEndPoint, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<SocketReceiveFromResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            var args = new SocketAsyncEventArgs { RemoteEndPoint = remoteEndPoint, SocketFlags = socketFlags };
            args.SetBuffer(buffer.Array, buffer.Offset, buffer.Count);

            args.Completed += OnCompleted;

            if (cancellationToken.IsCancellationRequested)
            {
                Cleanup(args);
                tcs.TrySetCanceled(cancellationToken);
                return new ValueTask<SocketReceiveFromResult>(tcs.Task);
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

            return new ValueTask<SocketReceiveFromResult>(tcs.Task.ContinueWith(task =>
            {
                registration.Dispose();
                return task.GetAwaiter().GetResult();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default));

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

#if !NET5_0_OR_GREATER
        public static ValueTask ConnectAsync(this Socket socket, EndPoint remoteEP, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => tcs.SetCanceled());
            try
            {
                socket.BeginConnect(remoteEP, AsyncCallback, null);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return new ValueTask(tcs.Task);

            void AsyncCallback(IAsyncResult ar)
            {
                try
                {
                    socket.EndConnect(ar);
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }
        }
#endif
    }
}
