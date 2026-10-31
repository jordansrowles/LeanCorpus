using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Reads canonical unsigned Int8 vector rows from a mapped scratch spool or the
/// separated metadata/code arrays at the end of a v3 <c>.vq</c> body.
/// </summary>
internal sealed class CanonicalInt8VectorReader : ICanonicalVectorSource, IDisposable
{
    private readonly IndexInput _input;
    private readonly int _docCount;
    private readonly int _dimension;
    private readonly byte[]? _presence;
    private readonly bool _interleaved;
    private readonly bool _ownsInput;
    private readonly long _recordStart;
    private readonly long _metadataStart;
    private readonly long _codesStart;
    private bool _disposed;

    private CanonicalInt8VectorReader(
        IndexInput input,
        int docCount,
        int dimension,
        byte[]? presence,
        bool interleaved,
        bool ownsInput,
        bool validateAllRows,
        long recordStart,
        long metadataStart,
        long codesStart)
    {
        _input = input;
        _docCount = docCount;
        _dimension = dimension;
        _presence = presence;
        _interleaved = interleaved;
        _ownsInput = ownsInput;
        _recordStart = recordStart;
        _metadataStart = metadataStart;
        _codesStart = codesStart;
        if (validateAllRows)
            ValidateMetadataRows();
    }

    /// <summary>Opens the temporary metadata-plus-codes spool written during a flush.</summary>
    internal static CanonicalInt8VectorReader OpenRecords(
        string filePath,
        int docCount,
        int dimension,
        byte[] presence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var input = new IndexInput(filePath);
        try
        {
            ValidateCommon(docCount, dimension, presence);
            long recordSize = checked((long)CanonicalInt8VectorMetadata.Size + dimension);
            long expectedLength = checked(docCount * recordSize);
            if (input.Length != expectedLength)
                throw new InvalidDataException("Canonical Int8 record spool length does not match its document count and dimension.");

            return new CanonicalInt8VectorReader(input, docCount, dimension, presence,
                interleaved: true, ownsInput: true, validateAllRows: true,
                recordStart: 0, metadataStart: 0, codesStart: 0);
        }
        catch
        {
            input.Dispose();
            throw;
        }
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

        return new CanonicalInt8VectorReader(input, docCount, dimension, presence,
            interleaved: false, ownsInput: false, validateAllRows: false,
            recordStart: 0, metadataStart, codesStart);
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

    /// <summary>Streams the exact metadata bytes for one row into a v3 body or merge destination.</summary>
    public void CopyCanonicalMetadataTo(int docId, ISequentialIndexOutput destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateDocId(docId);
        long position = MetadataOffset(docId);
        destination.WriteBytes(_input.BorrowSpan(CanonicalInt8VectorMetadata.Size, ref position));
    }

    /// <summary>Streams the exact code bytes for one row into a v3 body or merge destination.</summary>
    public void CopyCanonicalCodesTo(int docId, ISequentialIndexOutput destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ValidateDocId(docId);
        long position = CodesOffset(docId);
        destination.WriteBytes(_input.BorrowSpan(_dimension, ref position));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_ownsInput)
            _input.Dispose();
    }

    private long MetadataOffset(int docId)
        => _interleaved
            ? checked(_recordStart + (long)docId * ((long)CanonicalInt8VectorMetadata.Size + _dimension))
            : checked(_metadataStart + (long)docId * CanonicalInt8VectorMetadata.Size);

    private long CodesOffset(int docId)
        => _interleaved
            ? checked(_recordStart + (long)docId * ((long)CanonicalInt8VectorMetadata.Size + _dimension) + CanonicalInt8VectorMetadata.Size)
            : checked(_codesStart + (long)docId * _dimension);

    private void ValidateMetadataRows()
    {
        for (int docId = 0; docId < _docCount; docId++)
        {
            CanonicalInt8VectorMetadata metadata = GetCanonicalMetadata(docId);
            if (HasVector(docId))
                continue;

            long position = MetadataOffset(docId);
            ReadOnlySpan<byte> rawMetadata = _input.BorrowSpan(CanonicalInt8VectorMetadata.Size, ref position);
            foreach (byte value in rawMetadata)
            {
                if (value != 0)
                    throw new InvalidDataException("Missing canonical Int8 rows must have zero-filled metadata.");
            }

            if (metadata != default)
                throw new InvalidDataException("Missing canonical Int8 rows must have zero metadata values.");
            long codePosition = CodesOffset(docId);
            foreach (byte value in _input.BorrowSpan(_dimension, ref codePosition))
            {
                if (value != 0)
                    throw new InvalidDataException("Missing canonical Int8 rows must have zero-filled codes.");
            }
        }
    }

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
