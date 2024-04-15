#pragma warning disable CS8500

using KcpTransport.LowLevel;
using System.Buffers;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using static KcpTransport.LowLevel.KcpMethods;

namespace KcpTransport;


public interface ISocketOperation
{
    int Receive(Span<byte> buffer);
    int Send(ReadOnlySpan<byte> buffer);
}

public class SocketOperation(Socket socket) : ISocketOperation
{
    public int Receive(Span<byte> buffer) => socket.Receive(buffer);
    public int Send(ReadOnlySpan<byte> buffer) => socket.Send(buffer);
}

public class MemorySocketOperation : ISocketOperation
{
    public List<byte[]> SendData { get; } = new();
    public List<byte[]> ReceiveData { get; } = new();

    public int Receive(Span<byte> buffer)
    {
        ReceiveData.Add(buffer.ToArray());
        return buffer.Length;
    }

    public int Send(ReadOnlySpan<byte> buffer)
    {
        SendData.Add(buffer.ToArray());
        return buffer.Length;
    }
}


public class SimpleUpdServer
{

}


public sealed unsafe class KcpStream : Stream
{
    static readonly output_callback kcpOutputCallback = KcpOutputCallback;

    ISocketOperation socket;
    IKCPCB* kcp;
    byte[] receiveBuffer; // TODO: ???

    public KcpStream()
        : this(0, (ISocketOperation)null!)
    {
    }

    public KcpStream(uint conversationId, Socket socket)
        : this(conversationId, new SocketOperation(socket))
    {
    }

    public KcpStream(uint conversationId, ISocketOperation socket)
    {
        this.socket = socket;
        receiveBuffer = new byte[1024];
        kcp = ikcp_create(conversationId, this);
        kcp->output = kcpOutputCallback;

        ikcp_update(kcp, 0);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        // TODO:ReceiveFrom, IP?
        var size = socket.Receive(buffer.AsSpan(offset, count));
        fixed (byte* p = &buffer[offset])
        {
            ikcp_input(kcp, p, size); // return?
            return ikcp_recv(kcp, p, count);
        }
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        fixed (byte* p = &buffer[offset])
        {
            ikcp_send(kcp, p, count);
        }
    }

    public override void Flush()
    {
        ikcp_flush(kcp);
    }

    static int KcpOutputCallback(byte* buf, int len, IKCPCB* kcp, object user)
    {
        var self = (KcpStream)user;
        var buffer = new Span<byte>(buf, len);
        var sent = self.socket.Send(buffer); // TODO: sync, TODO: SendTo(IP?)
        return sent;
    }

    // TODO: dispose pattern

    protected override void Dispose(bool disposing)
    {
        ikcp_release(kcp);
    }

    #region NotSupported

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    #endregion
}
