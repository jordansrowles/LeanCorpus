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

    // Decompressed block cache (last used block)
    private int _cachedBlockIndex = -1;
    private byte[]? _cachedBlockData;
    private int[]? _cachedIntraOffsets;

    // Reusable MemoryStream + BinaryReader for ReadDocument
    private MemoryStream? _docStream;
    private BinaryReader? _docReader;

    private bool _disposed;

    private StoredFieldsReader(FileStream fs, BinaryReader reader, int blockSize, long[] blockOffsets, FieldCompressionPolicy compression)
    {
        _fs = fs;
        _reader = reader;
        _blockSize = blockSize;
        _blockOffsets = blockOffsets;
        _compression = compression;
    }

    public static StoredFieldsReader Open(string fdtPath, string fdxPath)
    {
        // Read .fdx to get block offsets
        using var fdxStream = new FileStream(fdxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var fdxReader = new BinaryReader(fdxStream, System.Text.Encoding.UTF8, leaveOpen: false);

        int firstInt = fdxReader.ReadInt32();
        int blockSize;
        int docCount;
        int blockCount;
        byte version;

        if (firstInt == CodecConstants.Magic)
        {
            version = fdxReader.ReadByte();
            if (version > CodecConstants.StoredFieldsVersion)
                throw new InvalidDataException(
                    $"Unsupported stored fields format version {version}. " +
                    $"This build supports up to version {CodecConstants.StoredFieldsVersion}. " +
                    "Please upgrade LeanCorpus.");

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

        var blockOffsets = new long[blockCount];
        for (int i = 0; i < blockCount; i++)
            blockOffsets[i] = fdxReader.ReadInt64();

        // Open .fdt and read its header to get compression type
        var fs = new FileStream(fdtPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: true);

        FieldCompressionPolicy compression;
        int fdtFirst = reader.ReadInt32();
        if (fdtFirst == CodecConstants.Magic)
        {
            byte fdtVersion = reader.ReadByte();
            int fdtBlockSize = reader.ReadInt32();
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
            reader.ReadByte(); // old version byte
            reader.ReadInt32(); // blockSize
            compression = FieldCompressionPolicy.Brotli;
        }

        return new StoredFieldsReader(fs, reader, blockSize, blockOffsets, compression);
    }

    public Dictionary<string, List<string>> ReadDocument(int docId)
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
        var br = _docReader!;

        int fieldCount = br.ReadInt32();
        var fields = new Dictionary<string, List<string>>(fieldCount);

        for (int i = 0; i < fieldCount; i++)
        {
            int nameLen = br.ReadInt32();
            string name = System.Text.Encoding.UTF8.GetString(br.ReadBytes(nameLen));

            int valueCount = br.ReadInt32();
            var values = new List<string>(valueCount);
            for (int v = 0; v < valueCount; v++)
            {
                int valueLen = br.ReadInt32();
                string value = System.Text.Encoding.UTF8.GetString(br.ReadBytes(valueLen));
                values.Add(value);
            }
            fields[name] = values;
        }

        return fields;
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
