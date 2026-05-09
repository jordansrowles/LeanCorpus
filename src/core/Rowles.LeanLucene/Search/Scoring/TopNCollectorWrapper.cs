namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Wraps a <see cref="TopNCollector"/> struct as an <see cref="ICollector"/> for
/// use in extensible search pipelines.
/// </summary>
public sealed class TopNCollectorWrapper : ICollector
{
    private TopNCollector _inner;

    /// <summary>Initialises a new <see cref="TopNCollectorWrapper"/> with the given maximum result count.</summary>
    /// <param name="maxSize">The maximum number of top-scoring documents to retain.</param>
    public TopNCollectorWrapper(int maxSize) => _inner = new TopNCollector(maxSize);

    /// <inheritdoc/>
    public int TotalHits => _inner.TotalHits;

    /// <inheritdoc/>
    public void Collect(int docId, float score) => _inner.Collect(docId, score);

    /// <summary>Materialises the collected results as a <see cref="TopDocs"/> sorted by score descending.</summary>
    /// <returns>A <see cref="TopDocs"/> containing the top-N scored documents.</returns>
    public TopDocs ToTopDocs() => _inner.ToTopDocs();
}
