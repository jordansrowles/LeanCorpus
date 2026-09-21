using System.Buffers;

namespace Rowles.LeanCorpus.Index.Indexer.Postings;

internal struct PostingSortDoc
{
    internal int NewDocId;
    internal int Freq;
    internal int PositionStart;
    internal int PositionCount;
}

internal struct PostingSortPosition
{
    internal int Position;
    internal int PayloadOffset;
    internal int PayloadLength;
    internal bool HasOffsets;
    internal int StartOffset;
    internal int EndOffset;
}

/// <summary>
/// Reusable pooled scratch for one index-sorted term at a time.
/// </summary>
internal sealed class IndexSortPostingScratch : IDisposable
{
    private const int InitialCapacity = 128;
    private readonly ArrayPool<PostingSortDoc> _docPool;
    private readonly ArrayPool<PostingSortPosition> _positionPool;
    private readonly ArrayPool<byte> _payloadPool;
    private int _disposed;

    internal PostingSortDoc[] Docs { get; private set; }
    internal PostingSortPosition[] Positions { get; private set; }
    internal byte[] PayloadBytes { get; private set; }
    internal int DocCount { get; private set; }
    internal int PositionCount { get; private set; }
    internal int PayloadCount { get; private set; }

    internal IndexSortPostingScratch(
        ArrayPool<PostingSortDoc>? docPool = null,
        ArrayPool<PostingSortPosition>? positionPool = null,
        ArrayPool<byte>? payloadPool = null)
    {
        _docPool = docPool ?? ArrayPool<PostingSortDoc>.Shared;
        _positionPool = positionPool ?? ArrayPool<PostingSortPosition>.Shared;
        _payloadPool = payloadPool ?? ArrayPool<byte>.Shared;
        Docs = _docPool.Rent(InitialCapacity);
        Positions = _positionPool.Rent(InitialCapacity);
        PayloadBytes = _payloadPool.Rent(InitialCapacity);
    }

    internal void Reset()
    {
        ThrowIfDisposed();
        DocCount = 0;
        PositionCount = 0;
        PayloadCount = 0;
    }

    internal void EnsureDocs(int required)
    {
        ThrowIfDisposed();
        if (required <= Docs.Length)
            return;
        var replacement = _docPool.Rent(Growth(Docs.Length, required));
        Docs.AsSpan(0, DocCount).CopyTo(replacement);
        _docPool.Return(Docs, clearArray: false);
        Docs = replacement;
    }

    internal void EnsurePositions(int required)
    {
        ThrowIfDisposed();
        if (required <= Positions.Length)
            return;
        var replacement = _positionPool.Rent(Growth(Positions.Length, required));
        Positions.AsSpan(0, PositionCount).CopyTo(replacement);
        _positionPool.Return(Positions, clearArray: false);
        Positions = replacement;
    }

    internal void EnsurePayloadBytes(int required)
    {
        ThrowIfDisposed();
        if (required <= PayloadBytes.Length)
            return;
        var replacement = _payloadPool.Rent(Growth(PayloadBytes.Length, required));
        PayloadBytes.AsSpan(0, PayloadCount).CopyTo(replacement);
        _payloadPool.Return(PayloadBytes, clearArray: false);
        PayloadBytes = replacement;
    }

    internal Span<byte> GetPayloadDestination(int length)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));
        EnsurePayloadBytes(checked(PayloadCount + length));
        return PayloadBytes.AsSpan(PayloadCount, length);
    }

    internal ref PostingSortDoc AppendDoc()
    {
        EnsureDocs(checked(DocCount + 1));
        return ref Docs[DocCount++];
    }

    internal ref PostingSortPosition AppendPosition()
    {
        EnsurePositions(checked(PositionCount + 1));
        return ref Positions[PositionCount++];
    }

    internal void AdvancePayload(int length)
    {
        if (length < 0 || PayloadCount > PayloadBytes.Length - length)
            throw new ArgumentOutOfRangeException(nameof(length));
        PayloadCount += length;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _docPool.Return(Docs, clearArray: false);
        _positionPool.Return(Positions, clearArray: false);
        _payloadPool.Return(PayloadBytes, clearArray: false);
        Docs = [];
        Positions = [];
        PayloadBytes = [];
        DocCount = 0;
        PositionCount = 0;
        PayloadCount = 0;
    }

    private static int Growth(int current, int required)
    {
        int doubled = current > int.MaxValue / 2 ? int.MaxValue : current * 2;
        return Math.Max(required, doubled);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
