namespace Rowles.LeanCorpus.Store;

/// <summary>Seekable stream adapter over an <see cref="IndexInput"/> slice.</summary>
internal sealed class IndexInputStream : Stream
{
    private readonly IndexInput _input;
    private readonly long _start;
    private readonly long _length;
    private readonly bool _leaveOpen;
    private bool _disposed;

    internal IndexInputStream(IndexInput input)
        : this(input, 0, input?.Length ?? throw new ArgumentNullException(nameof(input)), leaveOpen: false)
    {
    }

    internal IndexInputStream(IndexInput input, long start, long length, bool leaveOpen)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        if (start < 0 || length < 0 || start > input.Length || length > input.Length - start)
            throw new ArgumentOutOfRangeException(nameof(length), "The stream range is outside the input.");
        _start = start;
        _length = length;
        _leaveOpen = leaveOpen;
        _input.Seek(start);
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position
    {
        get => _input.Position - _start;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if ((uint)offset > (uint)buffer.Length || count < 0 || count > buffer.Length - offset)
            throw new ArgumentOutOfRangeException();
        int available = (int)Math.Min(count, _start + _length - _input.Position);
        if (available <= 0)
            return 0;
        _input.ReadSpan(available).CopyTo(buffer.AsSpan(offset, available));
        return available;
    }

    public override int Read(Span<byte> buffer)
    {
        int available = (int)Math.Min(buffer.Length, _start + _length - _input.Position);
        if (available <= 0)
            return 0;
        _input.ReadSpan(available).CopyTo(buffer);
        return available;
    }

    public override int ReadByte()
        => _input.Position < _start + _length ? _input.ReadByte() : -1;

    public override long Seek(long offset, SeekOrigin origin)
    {
        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(Position + offset),
            SeekOrigin.End => checked(_length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };
        if (position < 0 || position > _length)
            throw new IOException("The requested stream position is outside the compound member.");
        _input.Seek(_start + position);
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
            if (!_leaveOpen)
                _input.Dispose();
        }
        base.Dispose(disposing);
    }
}
