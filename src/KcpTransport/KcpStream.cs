#pragma warning disable CS8500

using System.Buffers;
using System.IO.Pipelines;

namespace KcpTransport;

public sealed unsafe class KcpStream : Stream, IBufferWriter<byte>
{
    KcpConnection connection;
    Pipe pipe;
    Stream pipeReaderStream;

    internal KcpStream(KcpConnection connection)
    {
        this.connection = connection;
        this.pipe = new Pipe();
        this.pipeReaderStream = pipe.Reader.AsStream();
    }

    // IBufferWriter

    public void Advance(int count)
    {
        pipe.Writer.Advance(count);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        return pipe.Writer.GetMemory(sizeHint);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        return pipe.Writer.GetSpan(sizeHint);
    }

    internal void WriterFlush()
    {
        _ = pipe.Writer.FlushAsync(); // TODO: async?
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
        return pipeReaderStream.ReadAsync(buffer, offset, count , cancellationToken);
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return pipeReaderStream.ReadAsync(buffer, cancellationToken);
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        connection.SendBuffer(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        connection.SendBuffer(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        connection.SendBuffer(buffer.AsSpan(offset, count));
        return Task.CompletedTask;
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        connection.SendBuffer(buffer.Span);
        return default;
    }

    public override void Flush()
    {
        connection.Flush();
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
