namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Creates stream views over already materialised local payloads.</summary>
public static class LocalStreamAdapters
{
    /// <summary>Creates a non-copying read-only stream over memory-backed data.</summary>
    public static Stream ReadOnly(ReadOnlyMemory<byte> data) =>
#if NET11_0_OR_GREATER
        new ReadOnlyMemoryStream(data);
#else
        new MemoryViewStream(data);

    private sealed class MemoryViewStream(ReadOnlyMemory<byte> data) : Stream
    {
        private long _position;
        private bool _disposed;
        public override bool CanRead => !_disposed;
        public override bool CanSeek => !_disposed;
        public override bool CanWrite => false;
        public override long Length { get { ThrowIfDisposed(); return data.Length; } }
        public override long Position
        {
            get { ThrowIfDisposed(); return _position; }
            set { ThrowIfDisposed(); ArgumentOutOfRangeException.ThrowIfNegative(value); _position = value; }
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            ThrowIfDisposed();
            if (_position >= data.Length) return 0;
            int count = Math.Min(buffer.Length, data.Length - (int)_position);
            data.Span.Slice((int)_position, count).CopyTo(buffer);
            _position += count;
            return count;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            long position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(_position + offset),
                SeekOrigin.End => checked(data.Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };
            if (position < 0) throw new IOException("Cannot seek before the beginning of the payload.");
            return _position = position;
        }
        public override void Flush() => ThrowIfDisposed();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { _disposed = true; base.Dispose(disposing); }
        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    }
#endif
}
