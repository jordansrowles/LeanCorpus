using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.StoredFields;

/// <summary>
/// Reads stored fields (.fdt) with registered block compression and multi-valued field support.
/// Paired with <see cref="StoredFieldsWriter"/>.
/// </summary>
internal sealed class StoredFieldsReader : IDisposable
{
    private readonly IndexInput _fdtInput;
    private readonly int _blockSize;
    private readonly int _docCount;
    private readonly long[] _blockOffsets;
    private readonly FieldCompressionPolicy _compression;
    private readonly long _bodyEnd;
    private readonly IDisposable _fdtFrame;

    private const int MaxCachedBlockCount = 8;
    private const int MaxCachedBlockBytes = 2 * 1024 * 1024;
    private const int MaxCachedBytes = 16 * 1024 * 1024;
    private readonly Lock _blockCachePublicationLock = new();
    private BlockCacheSnapshot _blockCache = BlockCacheSnapshot.Empty;
    private int _disposeStarted;

    /// <summary>Maximum decompressed byte size for a single stored fields block (256 MB).</summary>
    internal const int MaxDecompressedBlockBytes = 256 * 1024 * 1024;

    /// <summary>Maximum documents per block. Guards against corrupt headers.</summary>
    internal const int MaxBlockSize = 100_000;

    private StoredFieldsReader(
        IndexInput fdtInput,
        int blockSize,
        int docCount,
        long[] blockOffsets,
        FieldCompressionPolicy compression,
        long bodyEnd,
        IDisposable fdtFrame)
    {
        if (blockSize is < 1 or > MaxBlockSize)
            throw new InvalidDataException($"Stored fields block size {blockSize} is out of range [1, {MaxBlockSize}].");

        _fdtInput = fdtInput;
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
        => OpenPaths(fdtPath, fdxPath, requireMatchingVersions: true);

    internal static StoredFieldsReader OpenForMigration(string fdtPath, string fdxPath)
        => OpenPaths(fdtPath, fdxPath, requireMatchingVersions: false);

    private static StoredFieldsReader OpenPaths(string fdtPath, string fdxPath, bool requireMatchingVersions)
    {
        var fdtInput = new IndexInput(fdtPath);
        try
        {
            return Open(fdtInput, new IndexInput(fdxPath), requireMatchingVersions);
        }
        catch
        {
            fdtInput.Dispose();
            throw;
        }
    }

    internal static StoredFieldsReader Open(IndexInput fdtInput, IndexInput fdxInput)
        => Open(fdtInput, fdxInput, requireMatchingVersions: true);

    private static StoredFieldsReader Open(IndexInput fdtInput, IndexInput fdxInput, bool requireMatchingVersions)
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
            ValidateMatchingHeaders(".fdt", ".fdx", fdtFrame.Version, fdxVersion, fdtBlockSize, fdxBlockSize, requireMatchingVersions);
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

            var result = new StoredFieldsReader(
                fdtInput,
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
        int fdxBlockSize,
        bool requireMatchingVersions)
    {
        if (requireMatchingVersions && fdtVersion != fdxVersion)
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
        var block = GetBlockForDocument(docId, out int docInBlock);
        var cursor = new DocumentCursor(block.Data, block.IntraOffsets[docInBlock]);

        int fieldCount = cursor.ReadInt32();
        var fields = new Dictionary<string, List<StoredFieldValue>>(fieldCount, StringComparer.Ordinal);

        for (int i = 0; i < fieldCount; i++)
        {
            int nameLen = cursor.ReadInt32();
            string name = Encoding.UTF8.GetString(cursor.ReadSpan(nameLen));

            int valueCount = cursor.ReadInt32();

            if (fieldsToLoad is not null && !fieldsToLoad.Contains(name))
            {
                for (int v = 0; v < valueCount; v++)
                {
                    cursor.ReadByte(); // kind
                    int valueLength = cursor.ReadInt32();
                    cursor.Seek(valueLength, SeekOrigin.Current);
                }
                continue;
            }

            var values = new List<StoredFieldValue>(valueCount);
            for (int v = 0; v < valueCount; v++)
            {
                var kind = (StoredFieldValueKind)cursor.ReadByte();
                int valueLength = cursor.ReadInt32();
                if (kind == StoredFieldValueKind.Binary)
                {
                    values.Add(StoredFieldValue.FromBinary(cursor.ReadBytes(valueLength)));
                }
                else if (kind == StoredFieldValueKind.Long)
                {
                    var bytes = cursor.ReadSpan(valueLength);
                    long value = BinaryPrimitives.ReadInt64LittleEndian(bytes);
                    values.Add(StoredFieldValue.FromLong(value));
                }
                else
                {
                    values.Add(StoredFieldValue.FromString(Encoding.UTF8.GetString(cursor.ReadSpan(valueLength))));
                }
            }
            fields[name] = values;
        }

        return fields;
    }

    internal bool HasField(int docId, string field)
    {
        var block = GetBlockForDocument(docId, out int docInBlock);
        var cursor = new DocumentCursor(block.Data, block.IntraOffsets[docInBlock]);

        int fieldCount = cursor.ReadInt32();
        for (int i = 0; i < fieldCount; i++)
        {
            int nameLen = cursor.ReadInt32();
            string name = Encoding.UTF8.GetString(cursor.ReadSpan(nameLen));

            int valueCount = cursor.ReadInt32();
            if (string.Equals(name, field, StringComparison.Ordinal) && valueCount > 0)
                return true;
            for (int v = 0; v < valueCount; v++)
            {
                cursor.ReadByte(); // kind
                int valueLength = cursor.ReadInt32();
                cursor.Seek(valueLength, SeekOrigin.Current);
            }
        }

        return false;
    }

    private DecompressedBlock GetBlockForDocument(int docId, out int docInBlock)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId), docId, $"docId must be in the range [0, {_docCount}).");

        int blockIndex = docId / _blockSize;
        docInBlock = docId % _blockSize;
        return GetBlock(blockIndex);
    }

    private DecompressedBlock GetBlock(int blockIndex)
    {
        var cache = Volatile.Read(ref _blockCache);
        if (cache.Blocks.TryGetValue(blockIndex, out var cached))
            return cached;

        var loaded = DecompressBlock(blockIndex);
        if (loaded.CacheSize > MaxCachedBlockBytes || Volatile.Read(ref _disposeStarted) != 0)
            return loaded;

        lock (_blockCachePublicationLock)
        {
            if (Volatile.Read(ref _disposeStarted) != 0)
                return loaded;

            cache = _blockCache;
            if (cache.Blocks.TryGetValue(blockIndex, out cached))
                return cached;

            var blocks = new Dictionary<int, DecompressedBlock>(cache.Blocks)
            {
                [blockIndex] = loaded
            };
            var order = new List<int>(cache.InsertionOrder) { blockIndex };
            long cachedBytes = cache.CachedBytes + loaded.CacheSize;
            while (blocks.Count > MaxCachedBlockCount || cachedBytes > MaxCachedBytes)
            {
                int oldest = order[0];
                order.RemoveAt(0);
                if (blocks.Remove(oldest, out var removed))
                    cachedBytes -= removed.CacheSize;
            }

            Volatile.Write(ref _blockCache, new BlockCacheSnapshot(blocks, order.ToArray(), cachedBytes));
        }

        return loaded;
    }

    private DecompressedBlock DecompressBlock(int blockIndex)
    {
        int docCount;
        int rawLength;
        int compLength;
        int[] intraOffsets;
        byte[]? compData = null;
        try
        {
            long position = _blockOffsets[blockIndex];
            using (var session = _fdtInput.BeginReadSession())
            {
                if (position > _bodyEnd - 3L * sizeof(int))
                    throw new InvalidDataException("Stored fields block header extends beyond the data body.");

                docCount = session.ReadInt32(ref position);
                rawLength = session.ReadInt32(ref position);
                compLength = session.ReadInt32(ref position);

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

                long blockEnd = checked(position + (long)docCount * sizeof(int) + compLength);
                if (blockEnd > _bodyEnd)
                    throw new InvalidDataException("Stored fields block extends beyond the data body.");

                intraOffsets = new int[docCount];
                for (int i = 0; i < docCount; i++)
                    intraOffsets[i] = session.ReadInt32(ref position);

                compData = ArrayPool<byte>.Shared.Rent(compLength);
                session.BorrowSpan(compLength, ref position).CopyTo(compData);
            }

            var rawData = StoredFieldCompression.Decompress(
                compData!, compLength, rawLength, _compression);
            return new DecompressedBlock(rawData, intraOffsets);
        }
        finally
        {
            if (compData is not null)
                ArrayPool<byte>.Shared.Return(compData);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeStarted, 1) != 0)
            return;

        Volatile.Write(ref _blockCache, BlockCacheSnapshot.Empty);
        _fdtInput.Dispose();
        _fdtFrame.Dispose();
    }

    private sealed record DecompressedBlock(byte[] Data, int[] IntraOffsets)
    {
        internal long CacheSize => Data.Length + (long)IntraOffsets.Length * sizeof(int);
    }

    private sealed class BlockCacheSnapshot(
        Dictionary<int, DecompressedBlock> blocks,
        int[] insertionOrder,
        long cachedBytes)
    {
        internal static readonly BlockCacheSnapshot Empty = new([], [], 0);

        internal Dictionary<int, DecompressedBlock> Blocks { get; } = blocks;

        internal int[] InsertionOrder { get; } = insertionOrder;

        internal long CachedBytes { get; } = cachedBytes;
    }

    private ref struct DocumentCursor
    {
        private readonly byte[] _data;
        private int _position;

        internal DocumentCursor(byte[] data, int start)
        {
            if ((uint)start > (uint)data.Length)
                throw new IOException("The requested stream position is outside the stored fields block.");
            _data = data;
            _position = start;
        }

        internal int ReadInt32()
        {
            if (_position < 0 || _position > _data.Length - sizeof(int))
                throw new EndOfStreamException("Unable to read beyond the end of the stored fields document block.");
            int value = BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_position));
            _position += sizeof(int);
            return value;
        }

        internal byte ReadByte()
        {
            if ((uint)_position >= (uint)_data.Length)
                throw new EndOfStreamException("Unable to read beyond the end of the stored fields document block.");
            return _data[_position++];
        }

        internal byte[] ReadBytes(int count)
            => ReadSpan(count).ToArray();

        internal ReadOnlySpan<byte> ReadSpan(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            int available = Math.Max(0, _data.Length - _position);
            int bytesToRead = Math.Min(count, available);
            ReadOnlySpan<byte> result = _data.AsSpan(_position, bytesToRead);
            _position += bytesToRead;
            return result;
        }

        internal void Seek(int offset, SeekOrigin origin)
        {
            long position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => (long)_position + offset,
                SeekOrigin.End => (long)_data.Length + offset,
                _ => throw new ArgumentException("Invalid seek origin.", nameof(origin))
            };
            if (position < 0 || position > int.MaxValue)
                throw new IOException("An attempt was made to move the position before the beginning of the stored fields block.");
            _position = (int)position;
        }
    }
}
