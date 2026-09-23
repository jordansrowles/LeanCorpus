using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Reads the bounded tail directory and field sections of a packed BKD file.</summary>
internal sealed class PackedBkdReader : IDisposable
{
    private const int MaximumTreeDepth = 64;

    private readonly CodecReadSession _session;
    private readonly IndexInput _body;
    private readonly Dictionary<string, FieldDirectoryEntry> _directory;
    private readonly Dictionary<string, PackedBkdFieldMetadata> _metadata = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly long _directoryOffset;
    private bool _disposed;

    private PackedBkdReader(
        CodecReadSession session,
        IndexInput body,
        Dictionary<string, FieldDirectoryEntry> directory,
        long directoryOffset)
    {
        _session = session;
        _body = body;
        _directory = directory;
        _directoryOffset = directoryOffset;
    }

    internal static PackedBkdReader Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return OpenCore(new IndexInput(filePath));
    }

    internal static PackedBkdReader Open(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return OpenCore(input);
    }

    internal IReadOnlyCollection<string> FieldNames => _directory.Keys;

    internal bool HasField(string fieldName) => _directory.ContainsKey(fieldName);

    /// <summary>Verifies the source body once before a merge rewrites its values.</summary>
    internal void ValidateChecksum()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            try
            {
                _session.ValidateChecksum();
            }
            catch (CodecFileException exception)
            {
                throw new InvalidDataException("Packed BKD source checksum validation failed during merge.", exception);
            }
        }
    }

    internal PackedBkdFieldMetadata GetFieldMetadata(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (!_directory.TryGetValue(fieldName, out var entry))
                throw new KeyNotFoundException($"Packed BKD field '{fieldName}' is not present.");
            return _metadata.TryGetValue(fieldName, out var metadata)
                ? metadata
                : (_metadata[fieldName] = ParseField(fieldName, entry));
        }
    }

    /// <summary>Intersects one field with a visitor-defined packed query cell.</summary>
    internal bool Intersect<TVisitor>(string fieldName, ref TVisitor visitor)
        where TVisitor : struct, IPackedBkdIntersectVisitor
        => Intersect(fieldName, ref visitor, out _);

    /// <summary>Intersects one field and returns the traversal counters for an observer.</summary>
    internal bool Intersect<TVisitor>(
        string fieldName,
        ref TVisitor visitor,
        out PackedBkdTraversalStats stats)
        where TVisitor : struct, IPackedBkdIntersectVisitor
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        stats = default;
        using var activity = Diagnostics.LeanCorpusActivitySource.Source.StartActivity(
            Diagnostics.LeanCorpusActivitySource.PackedBkdIntersect);

        PackedBkdFieldMetadata metadata;
        lock (_gate)
        {
            if (!_directory.TryGetValue(fieldName, out var entry))
            {
                activity?.SetTag("packed_bkd.field_present", false);
                return false;
            }

            metadata = _metadata.TryGetValue(fieldName, out var cached)
                ? cached
                : (_metadata[fieldName] = ParseField(fieldName, entry));
        }

        using var queryBody = _body.OpenSharedSlice(0, _body.Length);
        using var cursor = new PackedBkdFieldCursor(queryBody, fieldName, metadata, MaximumTreeDepth);
        cursor.Intersect(ref visitor, ref stats);
        activity?.SetTag("packed_bkd.field_present", true);
        activity?.SetTag("packed_bkd.cells_visited", stats.CellsVisited);
        activity?.SetTag("packed_bkd.cells_pruned", stats.CellsPruned);
        activity?.SetTag("packed_bkd.leaves_visited", stats.LeavesVisited);
        activity?.SetTag("packed_bkd.leaves_semantically_validated", stats.LeavesSemanticallyValidated);
        activity?.SetTag("packed_bkd.packed_values_decoded", stats.PackedValuesDecoded);
        activity?.SetTag("packed_bkd.documents_visited", stats.DocumentsVisited);
        activity?.SetTag("packed_bkd.peak_leaf_scratch", stats.PeakLeafScratch);
        return true;
    }

    /// <summary>Checks the complete checksum, field tree and every encoded leaf.</summary>
    internal void DeepValidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        List<(string Name, PackedBkdFieldMetadata Metadata)> fields;
        lock (_gate)
        {
            _session.ValidateChecksum();
            ValidateCompleteSectionLayout();
            fields = new List<(string, PackedBkdFieldMetadata)>(_directory.Count);
            foreach (var (name, entry) in _directory)
            {
                var metadata = _metadata.TryGetValue(name, out var cached)
                    ? cached
                    : (_metadata[name] = ParseField(name, entry));
                fields.Add((name, metadata));
            }
        }

        foreach (var (name, metadata) in fields)
        {
            using var body = _body.OpenSharedSlice(0, _body.Length);
            using var cursor = new PackedBkdFieldCursor(body, name, metadata, MaximumTreeDepth);
            cursor.DeepValidate();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _body.Dispose();
        _session.Dispose();
    }

    private static PackedBkdReader OpenCore(IndexInput input)
    {
        CodecReadSession? session = null;
        IndexInput? body = null;
        try
        {
            session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
            body = session.OpenBodyInput();
            var directory = ReadDirectory(body);
            return new PackedBkdReader(session, body, directory.Entries, directory.DirectoryOffset);
        }
        catch
        {
            body?.Dispose();
            session?.Dispose();
            if (session is null)
                input.Dispose();
            throw;
        }
    }

    private static DirectoryReadResult ReadDirectory(IndexInput body)
    {
        if (body.Length < PackedBkdFormat.FooterLength)
            throw new InvalidDataException("Packed BKD body is shorter than its footer.");

        body.Seek(body.Length - PackedBkdFormat.FooterLength);
        uint magic = unchecked((uint)body.ReadInt32());
        if (magic != PackedBkdFormat.FooterMagic)
            throw new InvalidDataException("Packed BKD footer magic is invalid.");
        int footerFieldCount = body.ReadInt32();
        long directoryOffset = body.ReadInt64();
        if (footerFieldCount < 0
            || directoryOffset < 0
            || directoryOffset > body.Length - PackedBkdFormat.FooterLength)
            throw new InvalidDataException("Packed BKD footer metadata is outside the body.");

        body.Seek(directoryOffset);
        int fieldCount = ReadInt32(body, body.Length - PackedBkdFormat.FooterLength);
        if (fieldCount != footerFieldCount
            || fieldCount < 0
            || fieldCount > (body.Length - body.Position) / 17)
            throw new InvalidDataException("Packed BKD directory field count is invalid.");

        var result = new Dictionary<string, FieldDirectoryEntry>(fieldCount, StringComparer.Ordinal);
        string? previous = null;
        for (int i = 0; i < fieldCount; i++)
        {
            string name = PackedBkdFormat.ReadFieldName(body, body.Length - PackedBkdFormat.FooterLength);
            if (name.Length == 0 || (previous is not null && PackedBkdFieldNameComparer.Instance.Compare(previous, name) >= 0))
                throw new InvalidDataException("Packed BKD directory field names must be non-empty and strictly sorted.");
            long offset = ReadInt64(body, body.Length - PackedBkdFormat.FooterLength);
            long length = ReadInt64(body, body.Length - PackedBkdFormat.FooterLength);
            if (offset < 0 || length <= 0 || offset > directoryOffset || length > directoryOffset - offset)
                throw new InvalidDataException($"Packed BKD field '{name}' points outside its section area.");
            result.Add(name, new FieldDirectoryEntry(offset, length));
            previous = name;
        }

        if (body.Position != body.Length - PackedBkdFormat.FooterLength)
            throw new InvalidDataException("Packed BKD directory has trailing bytes.");

        var sections = result.Values.OrderBy(static entry => entry.Offset).ToArray();
        for (int i = 1; i < sections.Length; i++)
        {
            if (checked(sections[i - 1].Offset + sections[i - 1].Length) > sections[i].Offset)
                throw new InvalidDataException("Packed BKD field sections overlap.");
        }
        return new DirectoryReadResult(result, directoryOffset);
    }

    private PackedBkdFieldMetadata ParseField(string fieldName, FieldDirectoryEntry entry)
    {
        try
        {
            long sectionEnd = checked(entry.Offset + entry.Length);
            _body.Seek(entry.Offset);
            if (unchecked((uint)ReadInt32(_body, sectionEnd)) != PackedBkdFormat.FieldMagic)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid section magic.");

            int dimensions = ReadByte(_body, sectionEnd);
            int indexedDimensions = ReadByte(_body, sectionEnd);
            int bytesPerDimension = ReadByte(_body, sectionEnd);
            if (ReadByte(_body, sectionEnd) != 0)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has unknown section flags.");
            int maxPointsPerLeaf = ReadUInt16(_body, sectionEnd);
            if (ReadUInt16(_body, sectionEnd) != 0)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has non-zero reserved metadata.");
            int leafCount = ReadInt32(_body, sectionEnd);
            long pointCount = ReadInt64(_body, sectionEnd);
            int documentCount = ReadInt32(_body, sectionEnd);
            int splitCount = ReadInt32(_body, sectionEnd);

            var config = new PackedBkdConfig(dimensions, indexedDimensions, bytesPerDimension, maxPointsPerLeaf);
            long maximumPointCount = checked((long)leafCount * maxPointsPerLeaf);
            if (leafCount <= 0
                || pointCount < leafCount
                || pointCount > maximumPointCount
                || pointCount > int.MaxValue
                || documentCount <= 0
                || documentCount > pointCount)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid point or leaf counts.");
            if (splitCount != leafCount - 1)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has an inconsistent split count.");

            byte[] rootMinimum = ReadBytes(_body, sectionEnd, config.IndexedBytesLength);
            byte[] rootMaximum = ReadBytes(_body, sectionEnd, config.IndexedBytesLength);
            ValidateBounds(fieldName, "root", rootMinimum, rootMaximum, config);

            long splitDimensionsOffset = checked(_body.Position - entry.Offset);
            EnsureAvailable(_body, sectionEnd, splitCount);
            _body.Seek(checked(_body.Position + splitCount));
            long splitValuesOffset = checked(_body.Position - entry.Offset);
            long splitValuesBytes = checked((long)splitCount * config.BytesPerDimension);
            EnsureAvailable(_body, sectionEnd, splitValuesBytes);
            _body.Seek(checked(_body.Position + splitValuesBytes));

            long leafOffsetsOffset = checked(_body.Position - entry.Offset);
            long leafOffsetsBytes = checked((long)(leafCount + 1) * sizeof(long));
            EnsureAvailable(_body, sectionEnd, leafOffsetsBytes);
            _body.Seek(checked(_body.Position + leafOffsetsBytes));

            long leafDataOffset = checked(_body.Position - entry.Offset);
            long leafDataLength = checked(entry.Length - leafDataOffset);
            if (leafDataLength <= 0)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has no leaf data.");

            long firstLeafOffset = ReadRelativeLeafOffset(entry, leafOffsetsOffset, 0);
            long finalLeafOffset = ReadRelativeLeafOffset(entry, leafOffsetsOffset, leafCount);
            if (firstLeafOffset != 0 || finalLeafOffset != leafDataLength)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid top-level leaf data bounds.");

            return new PackedBkdFieldMetadata(
                config,
                pointCount,
                documentCount,
                leafCount,
                splitDimensionsOffset,
                splitValuesOffset,
                leafOffsetsOffset,
                leafDataOffset,
                leafDataLength,
                rootMinimum,
                rootMaximum,
                entry.Offset,
                entry.Length);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"Packed BKD field '{fieldName}' is truncated.", ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid configuration.", ex);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has overflowing metadata.", ex);
        }
    }

    private long ReadRelativeLeafOffset(FieldDirectoryEntry entry, long leafOffsetsOffset, int index)
    {
        long position = checked(entry.Offset + leafOffsetsOffset + (long)index * sizeof(long));
        _body.Seek(position);
        return ReadInt64(_body, checked(entry.Offset + entry.Length));
    }

    private void ValidateCompleteSectionLayout()
    {
        var sections = _directory.Values.OrderBy(static entry => entry.Offset).ToArray();
        long next = 0;
        foreach (var section in sections)
        {
            if (section.Offset != next)
                throw new InvalidDataException("Packed BKD field sections leave unparsed bytes in the body.");
            next = checked(section.Offset + section.Length);
        }
        if (next != _directoryOffset)
            throw new InvalidDataException("Packed BKD body has trailing or unparsed bytes before its directory.");
    }

    internal static int CompareDimension(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right, int dimension, int bytesPerDimension)
    {
        int offset = checked(dimension * bytesPerDimension);
        return left.Slice(offset, bytesPerDimension).SequenceCompareTo(right.Slice(offset, bytesPerDimension));
    }

    internal static void ValidateBounds(
        string fieldName,
        string boundsName,
        ReadOnlySpan<byte> minimum,
        ReadOnlySpan<byte> maximum,
        PackedBkdConfig config)
    {
        for (int dimension = 0; dimension < config.IndexedDimensions; dimension++)
        {
            if (CompareDimension(minimum, maximum, dimension, config.BytesPerDimension) > 0)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has inverted {boundsName} bounds in dimension {dimension}.");
        }
    }

    private static byte[] ReadBytes(IndexInput input, long sectionEnd, int count)
    {
        if (count < 0)
            throw new InvalidDataException("Packed BKD metadata has a negative byte count.");
        var bytes = new byte[count];
        ReadBytes(input, sectionEnd, bytes);
        return bytes;
    }

    private static void ReadBytes(IndexInput input, long sectionEnd, Span<byte> destination)
    {
        EnsureAvailable(input, sectionEnd, destination.Length);
        input.ReadBytes(destination);
    }

    private static int ReadByte(IndexInput input, long end)
    {
        if (input.Position >= end)
            throw new InvalidDataException("Packed BKD metadata exceeds its bounded section.");
        return input.ReadByte();
    }

    private static int ReadUInt16(IndexInput input, long end)
        => ReadByte(input, end) | (ReadByte(input, end) << 8);

    private static int ReadInt32(IndexInput input, long end)
    {
        EnsureAvailable(input, end, sizeof(int));
        return input.ReadInt32();
    }

    private static long ReadInt64(IndexInput input, long end)
    {
        EnsureAvailable(input, end, sizeof(long));
        return input.ReadInt64();
    }

    private static void EnsureAvailable(IndexInput input, long end, long count)
    {
        if (count < 0 || input.Position > end || count > end - input.Position)
            throw new InvalidDataException("Packed BKD metadata exceeds its bounded section.");
    }

    private readonly record struct FieldDirectoryEntry(long Offset, long Length);

    private readonly record struct DirectoryReadResult(
        Dictionary<string, FieldDirectoryEntry> Entries,
        long DirectoryOffset);
}
