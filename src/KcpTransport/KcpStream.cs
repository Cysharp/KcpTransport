#pragma warning disable CS8500

using System.Buffers;
using System.IO.Pipelines;

namespace KcpTransport;

public sealed unsafe class KcpStream : Stream
{
    KcpConnection connection;
    Pipe pipe;
    Stream pipeReaderStream;

    // write received, decoded kcp buffer to PipeStream
    internal PipeWriter InputWriter => pipe.Writer;

    internal KcpStream(KcpConnection connection)
    {
        this.connection = connection;
        this.pipe = new Pipe();
        this.pipeReaderStream = pipe.Reader.AsStream();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return pipeReaderStream.Read(buffer, offset, count);
    }

    public override int Read(Span<byte> buffer)
    {
        return pipeReaderStream.Read(buffer);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return pipeReaderStream.ReadAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return pipeReaderStream.ReadAsync(buffer, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        connection.SendReliableBuffer(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        connection.SendReliableBuffer(buffer);
    }

    public void WriteUnreliable(ReadOnlySpan<byte> buffer)
    {
        connection.SendUnreliableBuffer(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        connection.SendReliableBuffer(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        connection.SendReliableBuffer(buffer.Span);
        return default;
    }

    public ValueTask WriteUnreliableAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        connection.SendUnreliableBuffer(buffer.Span);
        return default;
    }

    public override void Flush()
    {
        connection.KcpFlush();
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        connection.KcpFlush();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        pipeReaderStream.Dispose();
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
