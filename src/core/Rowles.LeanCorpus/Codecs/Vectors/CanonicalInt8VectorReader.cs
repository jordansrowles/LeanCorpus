using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads the historical canonical unsigned Int8 arrays at the end of a v3 <c>.vq</c> body.
/// </summary>
internal sealed class CanonicalInt8VectorReader : IDisposable
{
    private readonly IndexInput _input;
    private readonly int _docCount;
    private readonly int _dimension;
    private readonly byte[] _presence;
    private readonly long _metadataStart;
    private readonly long _codesStart;
    private bool _disposed;

    private CanonicalInt8VectorReader(
        IndexInput input,
        int docCount,
        int dimension,
        byte[] presence,
        long metadataStart,
        long codesStart)
    {
        _input = input;
        _docCount = docCount;
        _dimension = dimension;
        _presence = presence;
        _metadataStart = metadataStart;
        _codesStart = codesStart;
    }

    /// <summary>
    /// Opens the canonical arrays at the end of a v3 <c>.vq</c> body. The enclosing
    /// quantised reader owns <paramref name="input"/> and controls its lifetime.
    /// </summary>
    internal static CanonicalInt8VectorReader OpenBody(
        IndexInput input,
        int docCount,
        int dimension,
        long metadataStart,
        long codesStart,
        long bodyEnd,
        byte[] presence)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateCommon(docCount, dimension, presence);
        if (metadataStart < 0 || codesStart < metadataStart || bodyEnd < codesStart || bodyEnd > input.Length)
            throw new InvalidDataException("Canonical Int8 arrays are outside the .vq body bounds.");

        long metadataEnd = checked(metadataStart + (long)docCount * CanonicalInt8VectorMetadata.Size);
        long codesEnd = checked(codesStart + (long)docCount * dimension);
        if (codesStart != metadataEnd || codesEnd != bodyEnd)
            throw new InvalidDataException("Canonical Int8 array offsets do not match the declared row counts.");

        return new CanonicalInt8VectorReader(input, docCount, dimension, presence, metadataStart, codesStart);
    }

    public int Count => _docCount;

    public int Dimension => _dimension;

    public bool HasVector(int docId)
        => (uint)docId < (uint)_docCount &&
           (_presence is null || (_presence[docId >> 3] & (1 << (docId & 7))) != 0);

    public ReadOnlySpan<float> GetVector(int docId) => ReadVector(docId);

    public float[] ReadVector(int docId)
    {
        var vector = new float[_dimension];
        CopyVectorTo(docId, vector);
        return vector;
    }

    public void CopyVectorTo(int docId, Span<float> destination)
    {
        ValidateDocId(docId);
        if (destination.Length != _dimension)
            throw new ArgumentException($"Destination length {destination.Length} != vector dimension {_dimension}.", nameof(destination));

        CanonicalInt8VectorMetadata metadata = GetCanonicalMetadata(docId);
        ReadOnlySpan<byte> codes = GetCanonicalCodes(docId);
        for (int index = 0; index < _dimension; index++)
            destination[index] = Decode(metadata.Offset, metadata.Scale, codes[index]);
    }

    public CanonicalInt8VectorMetadata GetCanonicalMetadata(int docId)
    {
        ValidateDocId(docId);
        long position = MetadataOffset(docId);
        var metadata = new CanonicalInt8VectorMetadata(
            _input.ReadSingle(ref position),
            _input.ReadSingle(ref position),
            _input.ReadInt32(ref position),
            _input.ReadSingle(ref position));
        ValidateMetadata(metadata, docId);
        return metadata;
    }

    public ReadOnlySpan<byte> GetCanonicalCodes(int docId)
    {
        ValidateDocId(docId);
        long position = CodesOffset(docId);
        return _input.BorrowSpan(_dimension, ref position);
    }

    /// <summary>Copies one raw metadata-plus-codes record without changing any stored bits.</summary>
    public void CopyCanonicalRecordTo(int docId, Span<byte> destination)
    {
        ValidateDocId(docId);
        int recordSize = checked(CanonicalInt8VectorMetadata.Size + _dimension);
        if (destination.Length != recordSize)
            throw new ArgumentException($"Destination length {destination.Length} != canonical record size {recordSize}.", nameof(destination));

        long metadataPosition = MetadataOffset(docId);
        _input.BorrowSpan(CanonicalInt8VectorMetadata.Size, ref metadataPosition)
            .CopyTo(destination[..CanonicalInt8VectorMetadata.Size]);
        long codesPosition = CodesOffset(docId);
        _input.BorrowSpan(_dimension, ref codesPosition)
            .CopyTo(destination[CanonicalInt8VectorMetadata.Size..]);
    }

    public void Dispose() => _disposed = true;

    private long MetadataOffset(int docId)
        => checked(_metadataStart + (long)docId * CanonicalInt8VectorMetadata.Size);

    private long CodesOffset(int docId)
        => checked(_codesStart + (long)docId * _dimension);

    private void ValidateMetadata(CanonicalInt8VectorMetadata metadata, int docId)
    {
        if (!float.IsFinite(metadata.Offset) || !float.IsFinite(metadata.Scale) || metadata.Scale < 0f ||
            !float.IsFinite(metadata.DecodedSquaredNorm) || metadata.DecodedSquaredNorm < 0f)
            throw new InvalidDataException($"Canonical Int8 metadata for document {docId} contains a non-finite or negative value.");

        long maximumCodeSum = Math.Min(int.MaxValue, (long)_dimension * byte.MaxValue);
        if (metadata.SumOfCodes < 0 || metadata.SumOfCodes > maximumCodeSum ||
            (metadata.Scale == 0f && metadata.SumOfCodes != 0))
            throw new InvalidDataException($"Canonical Int8 code sum for document {docId} is outside its supported range.");
    }

    private static float Decode(float offset, float scale, byte code)
        => (float)((double)offset + (double)scale * code);

    private void ValidateDocId(int docId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId));
    }

    private static void ValidateCommon(int docCount, int dimension, byte[]? presence)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(presence);
        if (presence.Length != VectorPresence.ByteCount(docCount))
            throw new InvalidDataException("Canonical Int8 presence length does not match its document count.");

        int remainder = docCount & 7;
        if (remainder != 0 && presence.Length > 0 && (presence[^1] & (0xff << remainder)) != 0)
            throw new InvalidDataException("Canonical Int8 presence bitmap has non-zero unused bits.");
    }
}
