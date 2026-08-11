using System.Buffers;
using System.IO;
using System.Text;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Reads stored fields (.fdt) with registered block compression and multi-valued field support.
/// Paired with <see cref="StoredFieldsWriter"/>.
/// </summary>
internal sealed class StoredFieldsReader : IDisposable
{
    private readonly Stream _stream;
    private readonly BinaryReader _reader;
    private readonly int _blockSize;
    private readonly int _docCount;
    private readonly long[] _blockOffsets;
    private readonly FieldCompressionPolicy _compression;
    private readonly long _bodyEnd;
    private readonly IDisposable _fdtFrame;

    // Decompressed block cache (last used block)
    private int _cachedBlockIndex = -1;
    private byte[]? _cachedBlockData;
    private int[]? _cachedIntraOffsets;

    // Reusable MemoryStream + BinaryReader for ReadDocument
    private MemoryStream? _docStream;
    private BinaryReader? _docReader;
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <summary>Maximum decompressed byte size for a single stored fields block (256 MB).</summary>
    internal const int MaxDecompressedBlockBytes = 256 * 1024 * 1024;

    /// <summary>Maximum documents per block. Guards against corrupt headers.</summary>
    internal const int MaxBlockSize = 100_000;

    private StoredFieldsReader(
        Stream stream,
        BinaryReader reader,
        int blockSize,
        int docCount,
        long[] blockOffsets,
        FieldCompressionPolicy compression,
        long bodyEnd,
        IDisposable fdtFrame)
    {
        if (blockSize is < 1 or > MaxBlockSize)
            throw new InvalidDataException($"Stored fields block size {blockSize} is out of range [1, {MaxBlockSize}].");

        _stream = stream;
        _reader = reader;
        _blockSize = blockSize;
        _docCount = docCount;
        _blockOffsets = blockOffsets;
        _compression = compression;
        _bodyEnd = bodyEnd;
        _fdtFrame = fdtFrame;
    }

    /// <summary>Number of documents indexed in this stored-fields file.</summary>
    internal int DocCount => _docCount;

    /// <summary>Compression policy used for the blocks in this file.</summary>
    internal FieldCompressionPolicy Compression => _compression;

    public static StoredFieldsReader Open(string fdtPath, string fdxPath)
    {
        var fdtInput = new IndexInput(fdtPath);
        try
        {
            return Open(fdtInput, new IndexInput(fdxPath));
        }
        catch
        {
            fdtInput.Dispose();
            throw;
        }
    }

    internal static StoredFieldsReader Open(IndexInput fdtInput, IndexInput fdxInput)
    {
        StoredFieldsReadFrame? fdtFrame = null;
        try
        {
            int fdxVersion;
            int fdxBlockSize;
            int docCount;
            long[] blockOffsets;
            using (fdxInput)
            using (var fdxFrame = StoredFieldsCodecFiles.OpenIndex(fdxInput))
            {
                fdxVersion = fdxFrame.Version;
                fdxBlockSize = fdxInput.ReadInt32();
                docCount = fdxInput.ReadInt32();
                int blockCount = fdxInput.ReadInt32();

                if (docCount < 0)
                    throw new InvalidDataException($"Stored fields index declares a negative document count {docCount}.");
                if (blockCount < 0 || blockCount > docCount)
                    throw new InvalidDataException($"Stored fields index block count {blockCount} is invalid for {docCount} documents.");
                long offsetsEnd = checked(fdxInput.Position + (long)blockCount * sizeof(long));
                if (offsetsEnd != fdxFrame.BodyEnd)
                    throw new InvalidDataException("Stored fields index length does not match its declared block count.");

                blockOffsets = new long[blockCount];
                for (int i = 0; i < blockCount; i++)
                    blockOffsets[i] = fdxInput.ReadInt64();
            }

            fdtFrame = StoredFieldsCodecFiles.OpenData(fdtInput);
            int fdtBlockSize = fdtInput.ReadInt32();
            ValidateMatchingHeaders(".fdt", ".fdx", fdtFrame.Version, fdxVersion, fdtBlockSize, fdxBlockSize);
            if (fdtBlockSize is < 1 or > MaxBlockSize)
                throw new InvalidDataException($"Stored fields block size {fdtBlockSize} is out of range [1, {MaxBlockSize}].");
            var compression = (FieldCompressionPolicy)fdtInput.ReadByte();

            if (!Enum.IsDefined(compression))
                throw new InvalidDataException($"Stored fields compression policy {(byte)compression} is unsupported.");
            int expectedBlockCount = docCount == 0 ? 0 : checked((docCount + fdtBlockSize - 1) / fdtBlockSize);
            if (blockOffsets.Length != expectedBlockCount)
                throw new InvalidDataException($"Stored fields index declares {blockOffsets.Length} blocks, but {expectedBlockCount} are required for {docCount} documents.");

            long firstBlockPosition = fdtInput.Position;
            long previousOffset = -1;
            foreach (long offset in blockOffsets)
            {
                if (offset < firstBlockPosition || offset >= fdtFrame.BodyEnd)
                    throw new InvalidDataException($"Stored fields block offset {offset} is outside the data body.");
                if (offset <= previousOffset)
                    throw new InvalidDataException("Stored fields block offsets must be strictly increasing.");
                previousOffset = offset;
            }

            var fs = new IndexInputStream(fdtInput);
            var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);
            var result = new StoredFieldsReader(
                fs,
                reader,
                fdtBlockSize,
                docCount,
                blockOffsets,
                compression,
                fdtFrame.BodyEnd,
                fdtFrame);
            fdtFrame = null;
            return result;
        }
        catch
        {
            fdtFrame?.Dispose();
            fdtInput.Dispose();
            fdxInput.Dispose();
            throw;
        }
    }

    private static void ValidateMatchingHeaders(
        string fdtPath,
        string fdxPath,
        int fdtVersion,
        int fdxVersion,
        int fdtBlockSize,
        int fdxBlockSize)
    {
        if (fdtVersion != fdxVersion)
        {
            throw new InvalidDataException(
                $"Mismatched stored fields versions between '{fdtPath}' and '{fdxPath}'.");
        }

        if (fdtBlockSize != fdxBlockSize)
        {
            throw new InvalidDataException(
                $"Mismatched stored fields block sizes between '{fdtPath}' and '{fdxPath}'.");
        }
    }

    public Dictionary<string, List<string>> ReadDocument(int docId)
    {
        var values = ReadDocumentValues(docId);
        var result = new Dictionary<string, List<string>>(values.Count, StringComparer.Ordinal);
        foreach (var (name, entries) in values)
        {
            var strings = new List<string>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry.IsBinary)
                    continue;

                if (entry.IsLong)
                {
                    strings.Add(entry.LongValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (entry.StringValue is not null)
                {
                    strings.Add(entry.StringValue);
                }
            }

            if (strings.Count > 0)
                result[name] = strings;
        }

        return result;
    }

    internal Dictionary<string, List<StoredFieldValue>> ReadDocumentValues(int docId)
    {
        return ReadDocumentValues(docId, null);
    }

    internal Dictionary<string, List<StoredFieldValue>> ReadDocumentValues(int docId, ISet<string>? fieldsToLoad)
    {
        lock (_lock)
        {
            var br = PositionDocumentReader(docId);

            int fieldCount = br.ReadInt32();
            var fields = new Dictionary<string, List<StoredFieldValue>>(fieldCount, StringComparer.Ordinal);

            for (int i = 0; i < fieldCount; i++)
            {
                int nameLen = br.ReadInt32();
                string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));

                int valueCount = br.ReadInt32();

                if (fieldsToLoad is not null && !fieldsToLoad.Contains(name))
                {
                    for (int v = 0; v < valueCount; v++)
                    {
                        br.ReadByte(); // kind
                        int valueLength = br.ReadInt32();
                        br.BaseStream.Seek(valueLength, SeekOrigin.Current);
                    }
                    continue;
                }

                var values = new List<StoredFieldValue>(valueCount);
                for (int v = 0; v < valueCount; v++)
                {
                    var kind = (StoredFieldValueKind)br.ReadByte();
                    int valueLength = br.ReadInt32();
                    if (kind == StoredFieldValueKind.Binary)
                    {
                        values.Add(StoredFieldValue.FromBinary(br.ReadBytes(valueLength)));
                    }
                    else if (kind == StoredFieldValueKind.Long)
                    {
                        var bytes = br.ReadBytes(valueLength);
                        long value = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(bytes);
                        values.Add(StoredFieldValue.FromLong(value));
                    }
                    else
                    {
                        values.Add(StoredFieldValue.FromString(System.Text.Encoding.UTF8.GetString(br.ReadBytes(valueLength))));
                    }
                }
                fields[name] = values;
            }

            return fields;
        }
    }

    internal bool HasField(int docId, string field)
    {
        lock (_lock)
        {
            var br = PositionDocumentReader(docId);

            int fieldCount = br.ReadInt32();
            for (int i = 0; i < fieldCount; i++)
            {
                int nameLen = br.ReadInt32();
                string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));

                int valueCount = br.ReadInt32();
                if (string.Equals(name, field, StringComparison.Ordinal) && valueCount > 0)
                    return true;
                for (int v = 0; v < valueCount; v++)
                {
                    var kind = (StoredFieldValueKind)br.ReadByte();
                    int valueLength = br.ReadInt32();
                    br.BaseStream.Seek(valueLength, SeekOrigin.Current);
                }
            }

            return false;
        }
    }

    private BinaryReader PositionDocumentReader(int docId)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId), docId, $"docId must be in the range [0, {_docCount}).");

        int blockIndex = docId / _blockSize;
        int docInBlock = docId % _blockSize;

        if (blockIndex != _cachedBlockIndex)
        {
            DecompressBlock(blockIndex);
            _docReader?.Dispose();
            _docStream?.Dispose();
            _docStream = new MemoryStream(_cachedBlockData!, 0, _cachedBlockData!.Length, writable: false, publiclyVisible: true);
            _docReader = new BinaryReader(_docStream, System.Text.Encoding.UTF8, leaveOpen: true);
        }
        else if (_docStream is null)
        {
            _docStream = new MemoryStream(_cachedBlockData!, 0, _cachedBlockData!.Length, writable: false, publiclyVisible: true);
            _docReader = new BinaryReader(_docStream, System.Text.Encoding.UTF8, leaveOpen: true);
        }

        _docStream!.Seek(_cachedIntraOffsets![docInBlock], SeekOrigin.Begin);
        return _docReader!;
    }

    private void DecompressBlock(int blockIndex)
    {
        _stream.Seek(_blockOffsets[blockIndex], SeekOrigin.Begin);

        if (_stream.Position > _bodyEnd - 3L * sizeof(int))
            throw new InvalidDataException("Stored fields block header extends beyond the data body.");

        int docCount = _reader.ReadInt32();
        int rawLength = _reader.ReadInt32();
        int compLength = _reader.ReadInt32();

        // Guard against corrupt or malicious block headers.
        if ((uint)docCount > (uint)_blockSize)
            throw new InvalidDataException($"Stored fields block has {docCount} documents but block size is {_blockSize}.");
        if (rawLength <= 0 || rawLength > MaxDecompressedBlockBytes)
            throw new InvalidDataException($"Stored fields block rawLength {rawLength} exceeds maximum {MaxDecompressedBlockBytes}.");
        if (compLength <= 0 || compLength > MaxDecompressedBlockBytes)
            throw new InvalidDataException($"Stored fields block compLength {compLength} exceeds maximum {MaxDecompressedBlockBytes}.");
        // Compression should not expand data beyond a 2x ratio; reject obvious bombs.
        if (compLength > rawLength * 2)
            throw new InvalidDataException($"Stored fields block compressed length {compLength} exceeds 2x raw length {rawLength}.");

        long blockEnd = checked(_stream.Position + (long)docCount * sizeof(int) + compLength);
        if (blockEnd > _bodyEnd)
            throw new InvalidDataException("Stored fields block extends beyond the data body.");

        var intraOffsets = new int[docCount];
        for (int i = 0; i < docCount; i++)
            intraOffsets[i] = _reader.ReadInt32();

        var compData = ArrayPool<byte>.Shared.Rent(compLength);
        try
        {
            _reader.BaseStream.ReadExactly(compData.AsSpan(0, compLength));

            var rawData = StoredFieldCompression.Decompress(
                compData, compLength, rawLength, _compression);

            _cachedBlockIndex = blockIndex;
            _cachedBlockData = rawData;
            _cachedIntraOffsets = intraOffsets;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(compData);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _docReader?.Dispose();
        _docStream?.Dispose();
        _reader.Dispose();
        _stream.Dispose();
        _fdtFrame.Dispose();
    }
}
