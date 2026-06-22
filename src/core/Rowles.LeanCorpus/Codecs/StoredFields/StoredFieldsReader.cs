using System.Buffers;

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

    private StoredFieldsReader(FileStream fs, BinaryReader reader, int blockSize, long[] blockOffsets, FieldCompressionPolicy compression, byte version)
    {
        _fs = fs;
        _reader = reader;
        _blockSize = blockSize;
        _blockOffsets = blockOffsets;
        _compression = compression;
        _version = version;
    }

    public static StoredFieldsReader Open(string fdtPath, string fdxPath)
    {
        // Read .fdx to get block offsets
        using var fdxStream = new FileStream(fdxPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        using var fdxReader = new BinaryReader(fdxStream, System.Text.Encoding.UTF8, leaveOpen: false);

        int firstInt = fdxReader.ReadInt32();
        int blockSize;
        int docCount;
        int blockCount;
        byte version;

        if (firstInt == CodecConstants.Magic)
        {
            version = fdxReader.ReadByte();
            blockSize = fdxReader.ReadInt32();
            docCount = fdxReader.ReadInt32();
            blockCount = fdxReader.ReadInt32();
        }
        else
        {
            fdxStream.Seek(0, SeekOrigin.Begin);
            version = fdxReader.ReadByte();
            blockSize = fdxReader.ReadInt32();
            docCount = fdxReader.ReadInt32();
            blockCount = fdxReader.ReadInt32();
        }

        ValidateSupportedVersion(version, "stored fields index (.fdx)");

        var blockOffsets = new long[blockCount];
        for (int i = 0; i < blockCount; i++)
            blockOffsets[i] = fdxReader.ReadInt64();

        // Open .fdt and read its header to get compression type
        var fs = new FileStream(fdtPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

        FieldCompressionPolicy compression;
        int fdtFirst = reader.ReadInt32();
        if (fdtFirst == CodecConstants.Magic)
        {
            byte fdtVersion = reader.ReadByte();
            int fdtBlockSize = reader.ReadInt32();
            ValidateSupportedVersion(fdtVersion, "stored fields data (.fdt)");
            ValidateMatchingHeaders(fdtPath, fdxPath, fdtVersion, version, fdtBlockSize, blockSize);
            if (fdtVersion >= 5)
            {
                compression = (FieldCompressionPolicy)reader.ReadByte();
            }
            else
            {
                // v4: Brotli
                compression = FieldCompressionPolicy.Brotli;
            }
        }
        else
        {
            // Pre-magic: Brotli
            fs.Seek(0, SeekOrigin.Begin);
            byte fdtVersion = reader.ReadByte();
            int fdtBlockSize = reader.ReadInt32();
            ValidateSupportedVersion(fdtVersion, "stored fields data (.fdt)");
            ValidateMatchingHeaders(fdtPath, fdxPath, fdtVersion, version, fdtBlockSize, blockSize);
            compression = FieldCompressionPolicy.Brotli;
        }

        return new StoredFieldsReader(fs, reader, blockSize, blockOffsets, compression, version);
    }

    private static void ValidateSupportedVersion(byte version, string fileType)
    {
        if (version > CodecConstants.StoredFieldsVersion)
        {
            throw new InvalidDataException(
                $"Unsupported {fileType} format version {version}. " +
                $"This build supports up to version {CodecConstants.StoredFieldsVersion}. " +
                "Please upgrade LeanCorpus.");
        }
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
                if (!entry.IsBinary && entry.StringValue is not null)
                    strings.Add(entry.StringValue);
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
                    if (_version >= 6)
                    {
                        br.ReadByte(); // kind
                        int valueLength = br.ReadInt32();
                        br.BaseStream.Seek(valueLength, SeekOrigin.Current);
                    }
                    else
                    {
                        int valueLength = br.ReadInt32();
                        br.BaseStream.Seek(valueLength, SeekOrigin.Current);
                    }
                }
                continue;
            }

            var values = new List<StoredFieldValue>(valueCount);
            for (int v = 0; v < valueCount; v++)
            {
                if (_version >= 6)
                {
                    var kind = (StoredFieldValueKind)br.ReadByte();
                    int valueLength = br.ReadInt32();
                    if (kind == StoredFieldValueKind.Binary)
                    {
                        values.Add(StoredFieldValue.FromBinary(br.ReadBytes(valueLength)));
                    }
                    else
                    {
                        values.Add(StoredFieldValue.FromString(System.Text.Encoding.UTF8.GetString(br.ReadBytes(valueLength))));
                    }
                    continue;
                }

                int valueLen = br.ReadInt32();
                values.Add(StoredFieldValue.FromString(System.Text.Encoding.UTF8.GetString(br.ReadBytes(valueLen))));
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
                int valueLength;
                if (_version >= 6)
                {
                    _ = br.ReadByte();
                    valueLength = br.ReadInt32();
                }
                else
                {
                    valueLength = br.ReadInt32();
                }

                br.BaseStream.Seek(valueLength, SeekOrigin.Current);
            }
        }

        return false;
    }

    private BinaryReader PositionDocumentReader(int docId)
    {
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
