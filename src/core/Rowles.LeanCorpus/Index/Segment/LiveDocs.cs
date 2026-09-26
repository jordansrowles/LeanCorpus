using System.IO;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Util;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Per-segment deletion tracker using a Roaring bitmap of deleted document IDs.
/// Sparse deletions use very little memory compared to the previous BitArray approach.
/// Optionally tracks soft-delete timestamps per deleted document for retention policies.
/// </summary>
internal sealed class LiveDocs
{
    private readonly RoaringBitmap _deletedDocs;
    private readonly int _maxDoc;

    /// <summary>Per-deleted-doc soft-delete timestamps (Unix milliseconds). Null when soft-deletes are not in use.</summary>
    private Dictionary<int, long>? _softDeleteTimestamps;

    public LiveDocs(int maxDoc)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDoc);
        _deletedDocs = new RoaringBitmap();
        _maxDoc = maxDoc;
    }

    private LiveDocs(RoaringBitmap deletedDocs, int maxDoc, Dictionary<int, long>? softDeleteTimestamps)
    {
        _deletedDocs = deletedDocs;
        _maxDoc = maxDoc;
        _softDeleteTimestamps = softDeleteTimestamps;
    }

    public int LiveCount => _maxDoc - _deletedDocs.Cardinality;
    public int MaxDoc => _maxDoc;
    public int DeletedCount => _deletedDocs.Cardinality;

    public void Delete(int docId)
    {
        ValidateDocId(docId);
        _deletedDocs.Add(docId);
    }

    /// <summary>
    /// Marks a document as soft-deleted with the given timestamp (Unix milliseconds).
    /// </summary>
    public void SoftDelete(int docId, long timestampMillis)
    {
        ValidateDocId(docId);
        _deletedDocs.Add(docId);
        _softDeleteTimestamps ??= new Dictionary<int, long>();
        _softDeleteTimestamps[docId] = timestampMillis;
    }

    private void ValidateDocId(int docId)
    {
        if ((uint)docId >= (uint)_maxDoc)
            throw new ArgumentOutOfRangeException(nameof(docId), docId,
                $"Document ID must be in the range [0, {_maxDoc}).");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public bool IsLive(int docId) => !_deletedDocs.Contains(docId);

    /// <summary>Returns the underlying deleted-docs bitmap for set operations.</summary>
    internal RoaringBitmap DeletedBitmap => _deletedDocs;

    /// <summary>
    /// Returns the soft-delete timestamps (keyed by doc ID), or null when none exist.
    /// </summary>
    internal Dictionary<int, long>? SoftDeleteTimestamps => _softDeleteTimestamps;

    /// <summary>
    /// Returns the earliest soft-delete timestamp (Unix millis) across all soft-deleted docs,
    /// or null if no soft-deleted documents exist.
    /// </summary>
    public long? EarliestSoftDeleteTimestamp
    {
        get
        {
            if (_softDeleteTimestamps is null or { Count: 0 })
                return null;

            long earliest = long.MaxValue;
            foreach (var ts in _softDeleteTimestamps.Values)
                if (ts < earliest) earliest = ts;
            return earliest == long.MaxValue ? null : earliest;
        }
    }

    /// <summary>
    /// Returns true if documents have been hard-deleted (deleted without a soft-delete timestamp).
    /// </summary>
    public bool HasHardDeletes => _deletedDocs.Cardinality > (_softDeleteTimestamps?.Count ?? 0);

    /// <summary>
    /// Writes live-doc state to <paramref name="filePath"/> atomically.
    /// Writes to a temporary file, optionally fsyncs, then renames over the target.
    /// Format: RoaringBitmap, then optional soft-delete section (int32 count, then count × (int32 docId, int64 ticks)).
    /// </summary>
    /// <param name="filePath">Destination path for the <c>.del</c> file.</param>
    /// <param name="liveDocs">The live-docs state to serialise.</param>
    /// <param name="durable">
    /// When <see langword="true"/> the stream is flushed to disk before the rename so
    /// the write is crash-safe. Matches the <c>IndexWriterConfig.DurableCommits</c> flag.
    /// </param>
    public static void Serialise(string filePath, LiveDocs liveDocs, bool durable = false)
    {
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.deletes.live-docs");
        CodecFileWriter.WriteAtomically(filePath, descriptor, durable, body =>
        {
            using var stream = body.AsStream();
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            liveDocs._deletedDocs.Serialise(writer);

            // Optional soft-delete section
            if (liveDocs._softDeleteTimestamps is { Count: > 0 } timestamps)
            {
                writer.Write(timestamps.Count);
                foreach (var (docId, ticks) in timestamps)
                {
                    writer.Write(docId);
                    writer.Write(ticks);
                }
            }
            else
            {
                writer.Write(0);
            }

            writer.Flush();
        });
    }

    /// <summary>
    /// Deserialises a <see cref="LiveDocs"/> from a <c>.del</c> file.
    /// Reads the RoaringBitmap first, then attempts to read an optional trailing
    /// soft-delete timestamp section.
    /// </summary>
    public static LiveDocs Deserialise(string filePath, int maxDoc)
    {
        return Deserialise(new IndexInput(filePath), maxDoc);
    }

    internal static LiveDocs Deserialise(IndexInput input, int maxDoc)
    {
        using var inputLifetime = input;
        if (input.Length >= sizeof(int))
        {
            int magic = input.ReadInt32();
            input.Seek(0);
            if (unchecked((uint)magic) == CodecFileWriter.Magic)
            {
                var descriptor = CodecCatalog.Default.GetFile("leancorpus.deletes.live-docs");
                using var frame = CodecFileReader.Open(input, descriptor);
                frame.ValidateChecksum();
                using var body = new IndexInputStream(input, frame.Metadata.BodyStart, frame.Metadata.BodyLength, leaveOpen: true);
                return DeserialiseBody(body, maxDoc);
            }
        }

        using var stream = new IndexInputStream(input, 0, input.Length, leaveOpen: true);
        return DeserialiseBody(stream, maxDoc);
    }

    private static LiveDocs DeserialiseBody(Stream stream, int maxDoc)
    {
        if (maxDoc < 0)
            throw new InvalidDataException($"Invalid maximum document count: {maxDoc}.");

        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        var deletedDocs = RoaringBitmap.Deserialise(reader);

        Dictionary<int, long>? timestamps = null;

        long remaining = stream.Length - stream.Position;
        if (remaining < 0)
            throw new InvalidDataException("Live-doc stream position is past the end of the stream.");

        // Headerless historical bodies may end directly after the bitmap. If a
        // timestamp trailer is present, however, it must be complete and exact.
        if (remaining != 0)
        {
            if (remaining < sizeof(int))
                throw new InvalidDataException("Live-doc soft-delete trailer is truncated before its count.");

            int sdCount = reader.ReadInt32();
            if (sdCount < 0)
                throw new InvalidDataException($"Invalid soft-delete timestamp count: {sdCount}.");
            if (sdCount > deletedDocs.Cardinality)
                throw new InvalidDataException(
                    $"Soft-delete timestamp count {sdCount} exceeds deleted-document count {deletedDocs.Cardinality}.");

            long expectedLength = sizeof(int) + (long)sdCount * (sizeof(int) + sizeof(long));
            if (remaining != expectedLength)
                throw new InvalidDataException(
                    $"Live-doc soft-delete trailer length is {remaining} bytes; expected {expectedLength} bytes.");

            if (sdCount > 0)
            {
                timestamps = new Dictionary<int, long>(sdCount);
                for (int i = 0; i < sdCount; i++)
                {
                    int docId = reader.ReadInt32();
                    long timestamp = reader.ReadInt64();
                    if ((uint)docId >= (uint)maxDoc)
                        throw new InvalidDataException(
                            $"Soft-delete timestamp document ID {docId} is outside [0, {maxDoc}).");
                    if (!deletedDocs.Contains(docId))
                        throw new InvalidDataException(
                            $"Soft-delete timestamp document ID {docId} is not marked deleted.");
                    if (!timestamps.TryAdd(docId, timestamp))
                        throw new InvalidDataException(
                            $"Live-doc soft-delete trailer contains duplicate document ID {docId}.");
                }
            }
        }

        foreach (var docId in deletedDocs)
        {
            if ((uint)docId >= (uint)maxDoc)
                throw new InvalidDataException(
                    $"Deleted document ID {docId} is outside [0, {maxDoc}).");
        }

        return new LiveDocs(deletedDocs, maxDoc, timestamps);
    }
}
