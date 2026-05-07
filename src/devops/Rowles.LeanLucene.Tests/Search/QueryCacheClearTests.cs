namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Additional unit tests for <see cref="QueryCache"/> covering the Clear method
/// and fingerprint paths not exercised by the main cache integration tests.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class QueryCacheClearTests
{
    // ── Clear ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Clear: Removes All Entries scenario.
    /// </summary>
    [Fact(DisplayName = "Clear: Removes All Entries")]
    public void Clear_RemovesAllEntries()
    {
        var cache = new QueryCache(50);
        cache.Put(new TermQuery("f", "a"), 10, TopDocs.Empty);
        cache.Put(new TermQuery("f", "b"), 10, TopDocs.Empty);

        cache.Clear();

        Assert.Equal(0, cache.Count);
    }

    /// <summary>
    /// Verifies the Clear: Resets Hit Counter scenario.
    /// </summary>
    [Fact(DisplayName = "Clear: Resets Hit Counter")]
    public void Clear_ResetsHitCounter()
    {
        var cache = new QueryCache(50);
        var q = new TermQuery("f", "a");
        cache.Put(q, 10, TopDocs.Empty);
        cache.TryGet(q, 10); // hit

        cache.Clear();

        Assert.Equal(0, cache.Hits);
    }

    /// <summary>
    /// Verifies the Clear: Resets Miss Counter scenario.
    /// </summary>
    [Fact(DisplayName = "Clear: Resets Miss Counter")]
    public void Clear_ResetsMissCounter()
    {
        var cache = new QueryCache(50);
        cache.TryGet(new TermQuery("f", "z"), 10); // miss

        cache.Clear();

        Assert.Equal(0, cache.Misses);
    }

    /// <summary>
    /// Verifies the Clear: Invalidates Previous Entries scenario.
    /// </summary>
    [Fact(DisplayName = "Clear: Invalidates Previous Entries")]
    public void Clear_InvalidatesPreviousEntries()
    {
        var cache = new QueryCache(50);
        var q = new TermQuery("f", "a");
        cache.Put(q, 10, TopDocs.Empty);
        cache.Clear();

        var result = cache.TryGet(q, 10);
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies the Clear: New Entries Can Be Added After Clear scenario.
    /// </summary>
    [Fact(DisplayName = "Clear: New Entries Can Be Added After Clear")]
    public void Clear_NewEntriesCanBeAddedAfterClear()
    {
        var cache = new QueryCache(50);
        cache.Put(new TermQuery("f", "old"), 10, TopDocs.Empty);
        cache.Clear();

        var q = new TermQuery("f", "new");
        cache.Put(q, 10, TopDocs.Empty);

        Assert.NotNull(cache.TryGet(q, 10));
    }

    // ── QueryCache constructor validation ─────────────────────────────────────

    /// <summary>
    /// Verifies the Constructor: Throws When Max Entries Less Than One scenario.
    /// </summary>
    [Fact(DisplayName = "Constructor: Throws When Max Entries Less Than One")]
    public void Constructor_ThrowsWhenMaxEntriesLessThanOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryCache(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QueryCache(-1));
    }

    // ── QueryFingerprint paths (via Put/TryGet) ───────────────────────────────

    /// <summary>
    /// Verifies the Cache: Disjunction Max Query Is Fingerprinted Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Cache: DisjunctionMaxQuery Is Fingerprinted Correctly")]
    public void Cache_DisjunctionMaxQuery_IsFingerprintedCorrectly()
    {
        var cache = new QueryCache(10);
        var q = new DisjunctionMaxQuery(0.2f);
        q.Add(new TermQuery("title", "foo"));
        q.Add(new TermQuery("body", "foo"));

        cache.Put(q, 5, TopDocs.Empty);

        // Same structure should hit
        var q2 = new DisjunctionMaxQuery(0.2f);
        q2.Add(new TermQuery("title", "foo"));
        q2.Add(new TermQuery("body", "foo"));

        Assert.NotNull(cache.TryGet(q2, 5));
    }

    /// <summary>
    /// Verifies the Cache: Constant Score Query Is Fingerprinted Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Cache: ConstantScoreQuery Is Fingerprinted Correctly")]
    public void Cache_ConstantScoreQuery_IsFingerprintedCorrectly()
    {
        var cache = new QueryCache(10);
        var q = new ConstantScoreQuery(new TermQuery("f", "t"), 3.0f);

        cache.Put(q, 5, TopDocs.Empty);

        var q2 = new ConstantScoreQuery(new TermQuery("f", "t"), 3.0f);
        Assert.NotNull(cache.TryGet(q2, 5));
    }

    /// <summary>
    /// Verifies the Cache: Range Query Is Fingerprinted Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Cache: RangeQuery Is Fingerprinted Correctly")]
    public void Cache_RangeQuery_IsFingerprintedCorrectly()
    {
        var cache = new QueryCache(10);
        var q = new RangeQuery("price", 10.0, 50.0);

        cache.Put(q, 5, TopDocs.Empty);

        var q2 = new RangeQuery("price", 10.0, 50.0);
        Assert.NotNull(cache.TryGet(q2, 5));
    }

    /// <summary>
    /// Verifies the Cache: Fuzzy Query Is Fingerprinted Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Cache: FuzzyQuery Is Fingerprinted Correctly")]
    public void Cache_FuzzyQuery_IsFingerprintedCorrectly()
    {
        var cache = new QueryCache(10);
        var q = new FuzzyQuery("body", "hello", maxEdits: 1);

        cache.Put(q, 5, TopDocs.Empty);

        var q2 = new FuzzyQuery("body", "hello", maxEdits: 1);
        Assert.NotNull(cache.TryGet(q2, 5));
    }

    /// <summary>
    /// Verifies the Cache: Block Join Query Is Fingerprinted Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Cache: BlockJoinQuery Is Fingerprinted Correctly")]
    public void Cache_BlockJoinQuery_IsFingerprintedCorrectly()
    {
        var cache = new QueryCache(10);
        var q = new BlockJoinQuery(new TermQuery("category", "shoes"));

        cache.Put(q, 5, TopDocs.Empty);

        var q2 = new BlockJoinQuery(new TermQuery("category", "shoes"));
        Assert.NotNull(cache.TryGet(q2, 5));
    }

    /// <summary>
    /// Verifies the Cache: Prefix Query Is Fingerprinted Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Cache: PrefixQuery Is Fingerprinted Correctly")]
    public void Cache_PrefixQuery_IsFingerprintedCorrectly()
    {
        var cache = new QueryCache(10);
        var q = new PrefixQuery("body", "hel");

        cache.Put(q, 5, TopDocs.Empty);

        var q2 = new PrefixQuery("body", "hel");
        Assert.NotNull(cache.TryGet(q2, 5));
    }

    /// <summary>
    /// Verifies the Cache: Wildcard Query Is Fingerprinted Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Cache: WildcardQuery Is Fingerprinted Correctly")]
    public void Cache_WildcardQuery_IsFingerprintedCorrectly()
    {
        var cache = new QueryCache(10);
        var q = new WildcardQuery("body", "hel*");

        cache.Put(q, 5, TopDocs.Empty);

        var q2 = new WildcardQuery("body", "hel*");
        Assert.NotNull(cache.TryGet(q2, 5));
    }
}
