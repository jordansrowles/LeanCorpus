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
    private readonly int[] _blockDocCounts;
    private readonly int[]? _blockDocStarts;
    private readonly FieldCompressionPolicy _compression;
    private readonly long _bodyEnd;
    private readonly IDisposable _fdtFrame;

    private const int MaxCachedBlockCount = 8;
    private const int MaxCachedBlockBytes = 2 * 1024 * 1024;
    private const int MaxCachedBytes = 16 * 1024 * 1024;
    private const int MaxInitialFieldCapacity = 16;
    private const int MaxInitialValueCapacity = 8;
    private readonly Lock _blockCachePublicationLock = new();
    private BlockCacheSnapshot _blockCache = BlockCacheSnapshot.Empty;
    private int _disposeStarted;

    /// <summary>Maximum decompressed byte size for a single stored fields block (256 MB).</summary>
    internal const int MaxDecompressedBlockBytes = StoredFieldsBlockPolicy.MaximumRawBytes;

    /// <summary>Maximum documents per block. Guards against corrupt headers.</summary>
    internal const int MaxBlockSize = StoredFieldsBlockPolicy.MaximumDocumentCount;

    private StoredFieldsReader(
        IndexInput fdtInput,
        int blockSize,
        int docCount,
        long[] blockOffsets,
        int[] blockDocCounts,
        int[]? blockDocStarts,
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
        _blockDocCounts = blockDocCounts;
        _blockDocStarts = blockDocStarts;
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

            bool variableBlockCounts = fdtFrame.Version >= 4;
            if (!variableBlockCounts)
            {
                int expectedBlockCount = docCount == 0 ? 0 : checked((docCount + fdtBlockSize - 1) / fdtBlockSize);
                if (blockOffsets.Length != expectedBlockCount)
                    throw new InvalidDataException($"Stored fields index declares {blockOffsets.Length} blocks, but {expectedBlockCount} are required for {docCount} documents.");
            }

            var (blockDocCounts, blockDocStarts) = ReadBlockDocumentCounts(
                fdtInput,
                blockOffsets,
                fdtFrame.BodyEnd,
                firstBlockPosition,
                fdtBlockSize,
                docCount,
                variableBlockCounts);

            var result = new StoredFieldsReader(
                fdtInput,
                fdtBlockSize,
                docCount,
                blockOffsets,
                blockDocCounts,
                blockDocStarts,
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

    private static (int[] BlockDocCounts, int[]? BlockDocStarts) ReadBlockDocumentCounts(
        IndexInput input,
        long[] blockOffsets,
        long bodyEnd,
        long firstBlockPosition,
        int maximumDocumentCount,
        int totalDocumentCount,
        bool variableBlockCounts)
    {
        var blockDocCounts = new int[blockOffsets.Length];
        int[]? blockDocStarts = variableBlockCounts ? new int[blockOffsets.Length] : null;
        int documentsRead = 0;

        using var session = input.BeginReadSession();
        for (int blockIndex = 0; blockIndex < blockOffsets.Length; blockIndex++)
        {
            long position = blockOffsets[blockIndex];
            if (position < firstBlockPosition || position > bodyEnd - 3L * sizeof(int))
                throw new InvalidDataException("Stored fields block header extends beyond the data body.");

            int blockDocCount = session.ReadInt32(ref position);
            int rawLength = session.ReadInt32(ref position);
            int compLength = session.ReadInt32(ref position);

            int expectedCount = variableBlockCounts
                ? maximumDocumentCount
                : Math.Min(maximumDocumentCount, totalDocumentCount - documentsRead);
            if (blockDocCount <= 0 ||
                (variableBlockCounts ? blockDocCount > expectedCount : blockDocCount != expectedCount) ||
                blockDocCount > totalDocumentCount - documentsRead)
                throw new InvalidDataException(
                    $"Stored fields block has {blockDocCount} documents but its allowed count is {expectedCount}.");
            if (rawLength <= 0 || rawLength > StoredFieldsBlockPolicy.MaximumRawBytes)
                throw new InvalidDataException(
                    $"Stored fields block rawLength {rawLength} exceeds maximum {StoredFieldsBlockPolicy.MaximumRawBytes}.");
            if (compLength <= 0 || compLength > StoredFieldsBlockPolicy.MaximumRawBytes)
                throw new InvalidDataException(
                    $"Stored fields block compLength {compLength} exceeds maximum {StoredFieldsBlockPolicy.MaximumRawBytes}.");
            if (variableBlockCounts && rawLength > StoredFieldsBlockPolicy.TargetRawBytes && blockDocCount != 1)
                throw new InvalidDataException(
                    $"Stored fields block rawLength {rawLength} exceeds target {StoredFieldsBlockPolicy.TargetRawBytes} for {blockDocCount} documents.");

            long blockEnd = checked(position + (long)blockDocCount * sizeof(int) + compLength);
            long nextBlockOffset = blockIndex + 1 < blockOffsets.Length ? blockOffsets[blockIndex + 1] : bodyEnd;
            if (blockEnd != nextBlockOffset)
                throw new InvalidDataException("Stored fields block size does not match its indexed boundary.");

            blockDocCounts[blockIndex] = blockDocCount;
            if (blockDocStarts is not null)
                blockDocStarts[blockIndex] = documentsRead;
            documentsRead = checked(documentsRead + blockDocCount);
        }

        if (documentsRead != totalDocumentCount)
            throw new InvalidDataException(
                $"Stored fields blocks contain {documentsRead} documents, but the index declares {totalDocumentCount}.");

        return (blockDocCounts, blockDocStarts);
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
        var block = GetBlockForDocument(docId, out int blockIndex, out int docInBlock);
        try
        {
            return ParseDocument(GetDocumentSpan(block, docInBlock), fieldsToLoad, fieldToFind: null, out _)!;
        }
        catch (InvalidDataException)
        {
            RemoveCachedBlock(blockIndex, block);
            throw;
        }
    }

    internal bool HasField(int docId, string field)
    {
        var block = GetBlockForDocument(docId, out int blockIndex, out int docInBlock);
        try
        {
            _ = ParseDocument(GetDocumentSpan(block, docInBlock), fieldsToLoad: null, fieldToFind: field, out bool hasField);
            return hasField;
        }
        catch (InvalidDataException)
        {
            RemoveCachedBlock(blockIndex, block);
            throw;
        }
    }

    private static Dictionary<string, List<StoredFieldValue>>? ParseDocument(
        ReadOnlySpan<byte> document,
        ISet<string>? fieldsToLoad,
        string? fieldToFind,
        out bool hasField)
    {
        var cursor = new DocumentCursor(document);
        int fieldCount = cursor.ReadCount("field", minimumRecordBytes: 2 * sizeof(int));
        var fields = fieldToFind is null
            ? new Dictionary<string, List<StoredFieldValue>>(Math.Min(fieldCount, MaxInitialFieldCapacity), StringComparer.Ordinal)
            : null;
        hasField = false;

        for (int i = 0; i < fieldCount; i++)
        {
            int nameLength = cursor.ReadLength("field name");
            string name = Encoding.UTF8.GetString(cursor.ReadSpan(nameLength));
            int valueCount = cursor.ReadCount("value", minimumRecordBytes: sizeof(byte) + sizeof(int));
            bool materialiseValues = fields is not null && (fieldsToLoad is null || fieldsToLoad.Contains(name));
            if (fieldToFind is not null && valueCount > 0 && string.Equals(name, fieldToFind, StringComparison.Ordinal))
                hasField = true;

            var values = materialiseValues
                ? new List<StoredFieldValue>(Math.Min(valueCount, MaxInitialValueCapacity))
                : null;
            for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
            {
                var kind = (StoredFieldValueKind)cursor.ReadByte();
                if (!Enum.IsDefined(kind))
                    throw new InvalidDataException($"Stored fields document contains unsupported value kind {(byte)kind}.");

                int valueLength = cursor.ReadLength("value");
                if (kind == StoredFieldValueKind.Long && valueLength != sizeof(long))
                    throw new InvalidDataException(
                        $"Stored fields Long value length {valueLength} is invalid; expected {sizeof(long)} bytes.");

                ReadOnlySpan<byte> payload = cursor.ReadSpan(valueLength);
                if (values is null)
                    continue;

                StoredFieldValue value = kind switch
                {
                    StoredFieldValueKind.String => StoredFieldValue.FromString(Encoding.UTF8.GetString(payload)),
                    StoredFieldValueKind.Binary => StoredFieldValue.FromBinary(payload),
                    StoredFieldValueKind.Long => StoredFieldValue.FromLong(BinaryPrimitives.ReadInt64LittleEndian(payload)),
                    _ => throw new InvalidDataException($"Stored fields document contains unsupported value kind {(byte)kind}.")
                };
                values.Add(value);
            }

            if (values is not null)
                fields![name] = values;
        }

        cursor.EnsureConsumed();
        return fields;
    }

    private static ReadOnlySpan<byte> GetDocumentSpan(DecompressedBlock block, int docInBlock)
    {
        int start = block.IntraOffsets[docInBlock];
        int end = docInBlock + 1 < block.IntraOffsets.Length
            ? block.IntraOffsets[docInBlock + 1]
            : block.Data.Length;
        if (start < 0 || end < start || end > block.Data.Length)
            throw new InvalidDataException("Stored fields document offsets are outside the decompressed block.");

        return block.Data.AsSpan(start, checked(end - start));
    }

    private void RemoveCachedBlock(int blockIndex, DecompressedBlock block)
    {
        lock (_blockCachePublicationLock)
        {
            var cache = _blockCache;
            if (!cache.Blocks.TryGetValue(blockIndex, out var cached) || !ReferenceEquals(cached, block))
                return;

            var blocks = new Dictionary<int, DecompressedBlock>(cache.Blocks);
            blocks.Remove(blockIndex);
            var order = new List<int>(Math.Max(0, cache.InsertionOrder.Length - 1));
            foreach (int cachedBlockIndex in cache.InsertionOrder)
            {
                if (cachedBlockIndex != blockIndex)
                    order.Add(cachedBlockIndex);
            }

            Volatile.Write(
                ref _blockCache,
                new BlockCacheSnapshot(blocks, order.ToArray(), cache.CachedBytes - cached.CacheSize));
        }
    }

    private DecompressedBlock GetBlockForDocument(int docId, out int blockIndex, out int docInBlock)
    {
        if ((uint)docId >= (uint)_docCount)
            throw new ArgumentOutOfRangeException(nameof(docId), docId, $"docId must be in the range [0, {_docCount}).");

        if (_blockDocStarts is null)
        {
            blockIndex = docId / _blockSize;
            docInBlock = docId % _blockSize;
        }
        else
        {
            blockIndex = Array.BinarySearch(_blockDocStarts, docId);
            if (blockIndex < 0)
                blockIndex = ~blockIndex - 1;
            docInBlock = docId - _blockDocStarts[blockIndex];
        }

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
                if (docCount != _blockDocCounts[blockIndex])
                    throw new InvalidDataException(
                        $"Stored fields block has {docCount} documents but its validated count is {_blockDocCounts[blockIndex]}.");
                if (rawLength <= 0 || rawLength > MaxDecompressedBlockBytes)
                    throw new InvalidDataException($"Stored fields block rawLength {rawLength} exceeds maximum {MaxDecompressedBlockBytes}.");
                if (compLength <= 0 || compLength > MaxDecompressedBlockBytes)
                    throw new InvalidDataException($"Stored fields block compLength {compLength} exceeds maximum {MaxDecompressedBlockBytes}.");
                if (_blockDocStarts is not null && rawLength > StoredFieldsBlockPolicy.TargetRawBytes && docCount != 1)
                    throw new InvalidDataException(
                        $"Stored fields block rawLength {rawLength} exceeds target {StoredFieldsBlockPolicy.TargetRawBytes} for {docCount} documents.");

                long blockEnd = checked(position + (long)docCount * sizeof(int) + compLength);
                long nextBlockOffset = blockIndex + 1 < _blockOffsets.Length
                    ? _blockOffsets[blockIndex + 1]
                    : _bodyEnd;
                if (blockEnd != nextBlockOffset)
                    throw new InvalidDataException("Stored fields block size does not match its indexed boundary.");

                intraOffsets = new int[docCount];
                for (int i = 0; i < docCount; i++)
                    intraOffsets[i] = session.ReadInt32(ref position);
                ValidateIntraOffsets(intraOffsets, rawLength);

                compData = ArrayPool<byte>.Shared.Rent(compLength);
                session.BorrowSpan(compLength, ref position).CopyTo(compData);
            }

            var rawData = StoredFieldCompression.Decompress(
                compData!, compLength, rawLength, _compression);
            if (rawData.Length != rawLength)
                throw new InvalidDataException(
                    $"Stored fields decompressor returned {rawData.Length} bytes; expected {rawLength} bytes.");
            return new DecompressedBlock(rawData, intraOffsets);
        }
        finally
        {
            if (compData is not null)
                ArrayPool<byte>.Shared.Return(compData);
        }
    }

    private static void ValidateIntraOffsets(ReadOnlySpan<int> intraOffsets, int rawLength)
    {
        if (intraOffsets.IsEmpty || intraOffsets[0] != 0)
            throw new InvalidDataException("The first stored fields document offset must be zero.");

        int previousOffset = -1;
        for (int i = 0; i < intraOffsets.Length; i++)
        {
            int offset = intraOffsets[i];
            if (offset < 0 || offset >= rawLength)
                throw new InvalidDataException(
                    $"Stored fields document offset {offset} is outside raw block length {rawLength}.");
            if (i > 0 && offset <= previousOffset)
                throw new InvalidDataException("Stored fields document offsets must be strictly increasing.");
            previousOffset = offset;
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
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        internal DocumentCursor(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        private int Remaining => _data.Length - _position;

        internal int ReadInt32()
        {
            if (Remaining < sizeof(int))
                throw new InvalidDataException("Stored fields document metadata extends beyond its document boundary.");
            int value = BinaryPrimitives.ReadInt32LittleEndian(_data[_position..]);
            _position += sizeof(int);
            return value;
        }

        internal int ReadCount(string description, int minimumRecordBytes)
        {
            int count = ReadInt32();
            if (count < 0 || count > Remaining / minimumRecordBytes)
                throw new InvalidDataException(
                    $"Stored fields {description} count {count} is invalid for {Remaining} remaining document bytes.");
            return count;
        }

        internal int ReadLength(string description)
        {
            int length = ReadInt32();
            if (length < 0 || length > Remaining)
                throw new InvalidDataException(
                    $"Stored fields {description} length {length} is outside the remaining {Remaining} document bytes.");
            return length;
        }

        internal byte ReadByte()
        {
            if (Remaining < sizeof(byte))
                throw new InvalidDataException("Stored fields document metadata extends beyond its document boundary.");
            return _data[_position++];
        }

        internal ReadOnlySpan<byte> ReadSpan(int count)
        {
            if (count < 0 || count > Remaining)
                throw new InvalidDataException(
                    $"Stored fields payload length {count} is outside the remaining {Remaining} document bytes.");
            ReadOnlySpan<byte> result = _data.Slice(_position, count);
            _position = checked(_position + count);
            return result;
        }

        internal void EnsureConsumed()
        {
            if (Remaining != 0)
                throw new InvalidDataException($"Stored fields document has {Remaining} unconsumed trailing bytes.");
        }
    }
}
