using Rowles.LeanLucene.Util;

namespace Rowles.LeanLucene.Index.Segment;

/// <summary>
/// Per-segment deletion tracker using a Roaring bitmap of deleted document IDs.
/// Sparse deletions use very little memory compared to the previous BitArray approach.
/// </summary>
internal sealed class LiveDocs
{
    private readonly RoaringBitmap _deletedDocs;
    private readonly int _maxDoc;

    public LiveDocs(int maxDoc)
    {
        _deletedDocs = new RoaringBitmap();
        _maxDoc = maxDoc;
    }

    private LiveDocs(RoaringBitmap deletedDocs, int maxDoc)
    {
        _deletedDocs = deletedDocs;
        _maxDoc = maxDoc;
    }

    public int LiveCount => _maxDoc - _deletedDocs.Cardinality;
    public int MaxDoc => _maxDoc;
    public int DeletedCount => _deletedDocs.Cardinality;

    public void Delete(int docId)
    {
        _deletedDocs.Add(docId);
    }

    public bool IsLive(int docId) => !_deletedDocs.Contains(docId);

    /// <summary>Returns the underlying deleted-docs bitmap for set operations.</summary>
    internal RoaringBitmap DeletedBitmap => _deletedDocs;

    /// <summary>
    /// Writes live-doc state to <paramref name="filePath"/> atomically.
    /// Writes to a temporary file, optionally fsyncs, then renames over the target.
    /// </summary>
    /// <param name="filePath">Destination path for the <c>.del</c> file.</param>
    /// <param name="liveDocs">The live-docs state to serialise.</param>
    /// <param name="durable">
    /// When <see langword="true"/> the stream is flushed to disk before the rename so
    /// the write is crash-safe. Matches the <c>IndexWriterConfig.DurableCommits</c> flag.
    /// </param>
    public static void Serialise(string filePath, LiveDocs liveDocs, bool durable = false)
    {
        var tmpPath = filePath + ".tmp";
        try
        {
            using (var stream = File.Create(tmpPath))
            using (var writer = new BinaryWriter(stream))
            {
                liveDocs._deletedDocs.Serialise(writer);
                if (durable)
                    stream.Flush(true);
            }

            File.Move(tmpPath, filePath, overwrite: true);
        }
        catch
        {
            // Best-effort cleanup of the temp file; ignore errors so the
            // original exception is not masked.
            try { File.Delete(tmpPath); } catch { }
            throw;
        }
    }

    public static LiveDocs Deserialise(string filePath, int maxDoc)
    {
        var deletedDocs = RoaringBitmap.Deserialise(filePath);
        return new LiveDocs(deletedDocs, maxDoc);
    }
}
