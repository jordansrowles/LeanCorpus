namespace Rowles.LeanCorpus.Store;

/// <summary>Seekable stream adapter over an <see cref="IndexInput"/> slice.</summary>
internal sealed class IndexInputStream : Stream
{
    private readonly IndexInput _input;
    private bool _disposed;

    internal IndexInputStream(IndexInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => _input.Length;
    public override long Position
    {
        get => _input.Position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)offset > (uint)buffer.Length || count < 0 || count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException();
        int available = (int)Math.Min(count, _input.Length - _input.Position);
        if (available <= 0)
            return 0;
        _input.ReadSpan(available).CopyTo(buffer.AsSpan(offset, available));
        return available;
    }

    public override int Read(Span<byte> buffer)
    {
        int available = (int)Math.Min(buffer.Length, _input.Length - _input.Position);
        if (available <= 0)
            return 0;
        _input.ReadSpan(available).CopyTo(buffer);
        return available;
    }

    public override int ReadByte()
        => _input.Position < _input.Length ? _input.ReadByte() : -1;

    public override long Seek(long offset, SeekOrigin origin)
    {
        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(_input.Position + offset),
            SeekOrigin.End => checked(_input.Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (position < 0 || position > _input.Length)
            throw new IOException("The requested stream position is outside the compound member.");
        _input.Seek(position);
        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _disposed = true;
            _input.Dispose();
        }
        base.Dispose(disposing);
    }
}
