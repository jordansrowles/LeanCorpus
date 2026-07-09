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
    private readonly FileStream _fs;
    private readonly BinaryReader _reader;
    private readonly int _blockSize;
    private readonly int _docCount;
    private readonly long[] _blockOffsets;
    private readonly FieldCompressionPolicy _compression;
    private readonly byte _version;

    // Decompressed block cache (last used block)
    private int _cachedBlockIndex = -1;
    private byte[]? _cachedBlockData;
    private int[]? _cachedIntraOffsets;

    // Reusable MemoryStream + BinaryReader for ReadDocument
    private MemoryStream? _docStream;
    private BinaryReader? _docReader;

    private bool _disposed;

    private StoredFieldsReader(
        FileStream fs,
        BinaryReader reader,
        int blockSize,
        int docCount,
        long[] blockOffsets,
        FieldCompressionPolicy compression,
        byte version)
    {
        _fs = fs;
        _reader = reader;
        _blockSize = blockSize;
        _docCount = docCount;
        _blockOffsets = blockOffsets;
        _compression = compression;
        _version = version;
    }

    /// <summary>Number of documents indexed in this stored-fields file.</summary>
    internal int DocCount => _docCount;

    /// <summary>Compression policy used for the blocks in this file.</summary>
    internal FieldCompressionPolicy Compression => _compression;

    public static StoredFieldsReader Open(string fdtPath, string fdxPath)
    {
        // Read .fdx to get block offsets
        using var fdxStream = FileOpenRetry.OpenReadDelete(fdxPath);
        using var fdxReader = new BinaryReader(fdxStream, System.Text.Encoding.UTF8, leaveOpen: false);

        byte fdxVersion = StoredFieldsFileHeader.ReadVersion(fdxReader);
        if (fdxVersion > StoredFieldsFileHeader.V2)
        {
            throw new InvalidDataException(
                $"Unsupported stored fields index (.fdx) format version {fdxVersion}. " +
                $"This build supports up to version {StoredFieldsFileHeader.V2}.");
        }

        int fdxBlockSize = fdxReader.ReadInt32();
        int docCount = fdxReader.ReadInt32();
        int blockCount = fdxReader.ReadInt32();

        var blockOffsets = new long[blockCount];
        for (int i = 0; i < blockCount; i++)
            blockOffsets[i] = fdxReader.ReadInt64();

        // Open .fdt and read its header to get compression type
        var fs = FileOpenRetry.OpenReadDelete(fdtPath);
        var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

        fs.Seek(0, SeekOrigin.Begin);
        byte fdtVersion = StoredFieldsFileHeader.ReadVersion(reader);
        if (fdtVersion > StoredFieldsFileHeader.V2)
        {
            throw new InvalidDataException(
                $"Unsupported stored fields data (.fdt) format version {fdtVersion}. " +
                $"This build supports up to version {StoredFieldsFileHeader.V2}.");
        }

        int fdtBlockSize = reader.ReadInt32();
        ValidateMatchingHeaders(fdtPath, fdxPath, fdtVersion, fdxVersion, fdtBlockSize, fdxBlockSize);
        var compression = (FieldCompressionPolicy)reader.ReadByte();

        return new StoredFieldsReader(fs, reader, fdtBlockSize, docCount, blockOffsets, compression, fdtVersion);
    }

    private static void ValidateMatchingHeaders(
        string fdtPath,
        string fdxPath,
        byte fdtVersion,
        byte fdxVersion,
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

    internal bool HasField(int docId, string field)
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
        _fs.Seek(_blockOffsets[blockIndex], SeekOrigin.Begin);

        int docCount = _reader.ReadInt32();
        int rawLength = _reader.ReadInt32();
        int compLength = _reader.ReadInt32();

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
        _fs.Dispose();
    }
}
