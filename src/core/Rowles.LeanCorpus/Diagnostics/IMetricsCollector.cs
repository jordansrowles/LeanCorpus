namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// Interface for collecting operational metrics from IndexSearcher, IndexWriter, and QueryCache.
/// </summary>
public interface IMetricsCollector
{
    /// <summary>Records a search latency sample.</summary>
    void RecordSearchLatency(TimeSpan elapsed);

    /// <summary>Records a query cache hit.</summary>
    void RecordCacheHit();

    /// <summary>Records a query cache miss.</summary>
    void RecordCacheMiss();

    /// <summary>Records a segment flush event.</summary>
    void RecordFlush(TimeSpan elapsed);

    /// <summary>Records a segment merge event.</summary>
    void RecordMerge(TimeSpan elapsed, int segmentsMerged);

    /// <summary>Records a commit event.</summary>
    void RecordCommit(TimeSpan elapsed);

    /// <summary>Records physical file synchronisation performed by a durable commit.</summary>
    void RecordFileSync(TimeSpan elapsed, long bytes, int fileCount) { }

    /// <summary>Records one codec contribution to a segment flush.</summary>
    void RecordCodecFlush(string codec, TimeSpan elapsed, long bytesWritten) { }

    /// <summary>Records writer-owned active and flushing memory.</summary>
    void RecordWriterMemory(long activeBytes, long flushingBytes, long deleteBytes) { }

    /// <summary>Records a delete-application phase.</summary>
    void RecordDeleteApplication(TimeSpan elapsed, int terms, int changedSegments) { }

    /// <summary>Records queued merge work and producer stall time.</summary>
    void RecordMergeBacklog(long pendingBytes, TimeSpan producerStall) { }

    /// <summary>
    /// Records a single HNSW graph traversal. <paramref name="nodesVisited"/> is the number of
    /// distinct nodes visited during the layer-zero search and is the primary recall-vs-cost signal.
    /// Default implementation is a no-op for backwards compatibility.
    /// </summary>
    void RecordHnswSearch(TimeSpan elapsed, int nodesVisited) { }

    /// <summary>
    /// Records a single HNSW graph build (flush or merge). <paramref name="nodes"/> is the number
    /// of vectors inserted. Default implementation is a no-op for backwards compatibility.
    /// </summary>
    void RecordHnswBuild(TimeSpan elapsed, int nodes) { }

    /// <summary>Takes a point-in-time snapshot of all metrics.</summary>
    MetricsSnapshot GetSnapshot();
}
