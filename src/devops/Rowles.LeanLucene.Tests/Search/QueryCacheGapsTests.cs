namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Additional QueryCache tests covering fingerprint paths not exercised elsewhere:
/// FunctionScoreQuery, RrfQuery, VectorQuery (with and without filter),
/// the default-fallback path, and the Put duplicate-key replacement path.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class QueryCacheGapsTests
{
    // ── Fingerprint paths ─────────────────────────────────────────────────────

    /// <summary>Verifies FunctionScoreQuery can be stored and retrieved from cache.</summary>
    [Fact(DisplayName = "Cache: FunctionScoreQuery Fingerprint Stored And Retrieved")]
    public void Cache_FunctionScoreQuery_StoredAndRetrieved()
    {
        var cache = new QueryCache(50);
        var q = new FunctionScoreQuery(new TermQuery("body", "word"), "rank", ScoreMode.Multiply);
        cache.Put(q, 10, TopDocs.Empty);
        Assert.NotNull(cache.TryGet(q, 10));
    }

    /// <summary>Verifies RrfQuery can be stored and retrieved from cache.</summary>
    [Fact(DisplayName = "Cache: RrfQuery Fingerprint Stored And Retrieved")]
    public void Cache_RrfQuery_StoredAndRetrieved()
    {
        var cache = new QueryCache(50);
        var q = new RrfQuery(60);
        q.Add(new TermQuery("title", "hello"));
        q.Add(new TermQuery("body", "world"));
        cache.Put(q, 10, TopDocs.Empty);
        Assert.NotNull(cache.TryGet(q, 10));
    }

    /// <summary>Verifies VectorQuery without filter can be stored and retrieved from cache.</summary>
    [Fact(DisplayName = "Cache: VectorQuery Without Filter Stored And Retrieved")]
    public void Cache_VectorQuery_NoFilter_StoredAndRetrieved()
    {
        var cache = new QueryCache(50);
        var q = new VectorQuery("embedding", new float[] { 0.1f, 0.2f, 0.3f }, topK: 5);
        cache.Put(q, 5, TopDocs.Empty);
        Assert.NotNull(cache.TryGet(q, 5));
    }

    /// <summary>Verifies VectorQuery with a filter can be stored and retrieved from cache.</summary>
    [Fact(DisplayName = "Cache: VectorQuery With Filter Stored And Retrieved")]
    public void Cache_VectorQuery_WithFilter_StoredAndRetrieved()
    {
        var cache = new QueryCache(50);
        var filter = new TermQuery("category", "news");
        var q = new VectorQuery("embedding", new float[] { 0.5f, 0.6f }, topK: 3, filter: filter);
        cache.Put(q, 3, TopDocs.Empty);
        Assert.NotNull(cache.TryGet(q, 3));
    }

    /// <summary>
    /// Verifies TermRangeQuery falls through to the default fingerprint path
    /// and is still stored and retrieved correctly.
    /// </summary>
    [Fact(DisplayName = "Cache: TermRangeQuery Uses Default Fingerprint Path")]
    public void Cache_TermRangeQuery_DefaultFingerprintPath()
    {
        var cache = new QueryCache(50);
        var q = new TermRangeQuery("fruit", "apple", "mango");
        cache.Put(q, 10, TopDocs.Empty);
        Assert.NotNull(cache.TryGet(q, 10));
    }

    // ── Put duplicate-key replacement ─────────────────────────────────────────

    /// <summary>
    /// Verifies that putting the same query+topN a second time replaces the existing entry
    /// rather than duplicating it.
    /// </summary>
    [Fact(DisplayName = "Cache: Put Duplicate Key Replaces Existing Entry")]
    public void Cache_PutDuplicateKey_ReplacesExistingEntry()
    {
        var cache = new QueryCache(50);
        var q = new TermQuery("f", "hello");
        var first = new TopDocs(1, [new ScoreDoc(1, 1.0f)]);
        var second = new TopDocs(2, [new ScoreDoc(2, 2.0f)]);

        cache.Put(q, 10, first);
        cache.Put(q, 10, second);

        Assert.Equal(1, cache.Count);
        var result = cache.TryGet(q, 10);
        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalHits);
    }

    // ── Hits / Misses property getters ────────────────────────────────────────

    /// <summary>Verifies Hits increments on a successful TryGet call.</summary>
    [Fact(DisplayName = "Cache: Hits Counter Increments On Hit")]
    public void Cache_HitsCounter_IncrementsOnHit()
    {
        var cache = new QueryCache(50);
        var q = new TermQuery("f", "a");
        cache.Put(q, 10, TopDocs.Empty);
        cache.TryGet(q, 10);
        Assert.Equal(1, cache.Hits);
    }

    /// <summary>Verifies Misses increments on a failed TryGet call.</summary>
    [Fact(DisplayName = "Cache: Misses Counter Increments On Miss")]
    public void Cache_MissesCounter_IncrementsOnMiss()
    {
        var cache = new QueryCache(50);
        cache.TryGet(new TermQuery("f", "notfound"), 10);
        Assert.Equal(1, cache.Misses);
    }
}
