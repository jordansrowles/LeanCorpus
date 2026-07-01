using System.IO;
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
        _deletedDocs.Add(docId);
    }

    /// <summary>
    /// Marks a document as soft-deleted with the given timestamp (Unix milliseconds).
    /// </summary>
    public void SoftDelete(int docId, long timestampMillis)
    {
        _deletedDocs.Add(docId);
        _softDeleteTimestamps ??= new Dictionary<int, long>();
        _softDeleteTimestamps[docId] = timestampMillis;
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
        IndexAtomicFileWriter.Write(filePath, durable, stream =>
        {
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
        using var fs = FileOpenRetry.OpenReadDelete(filePath);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8);

        var deletedDocs = RoaringBitmap.Deserialise(reader);

        Dictionary<int, long>? timestamps = null;

        // Check if there is trailing data after the bitmap
        long remaining = fs.Length - fs.Position;
        if (remaining >= 4)
        {
            try
            {
                int sdCount = reader.ReadInt32();
                if (sdCount > 0 && remaining >= 4 + (long)sdCount * 12)
                {
                    timestamps = new Dictionary<int, long>(sdCount);
                    for (int i = 0; i < sdCount; i++)
                    {
                        int docId = reader.ReadInt32();
                        long ticks = reader.ReadInt64();
                        timestamps[docId] = ticks;
                    }
                }
            }
            catch (EndOfStreamException)
            {
                // Graceful fallback for truncated soft-delete section
            }

            // Remove timestamps for doc IDs that are not in the deleted bitmap
            if (timestamps is not null)
            {
                var orphanKeys = new List<int>();
                foreach (var kvp in timestamps)
                {
                    if (!deletedDocs.Contains(kvp.Key))
                        orphanKeys.Add(kvp.Key);
                }
                foreach (var key in orphanKeys)
                    timestamps.Remove(key);
            }
        }

        // Strip out-of-range doc IDs from the deleted bitmap
        if (maxDoc > 0)
        {
            var outOfRangeDocs = new List<int>();
            foreach (var docId in deletedDocs)
            {
                if ((uint)docId >= (uint)maxDoc)
                    outOfRangeDocs.Add(docId);
            }
            foreach (var docId in outOfRangeDocs)
                deletedDocs.Remove(docId);
        }

        return new LiveDocs(deletedDocs, maxDoc, timestamps);
    }
}
