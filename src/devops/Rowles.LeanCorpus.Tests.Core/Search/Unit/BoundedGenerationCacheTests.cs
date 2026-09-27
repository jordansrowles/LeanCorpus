using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Search.Searcher;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class BoundedGenerationCacheTests
{
    [Fact]
    public void GetOrAdd_ConcurrentUniqueKeysEvictWholeGenerations()
    {
        const int maximumEntries = 16;
        var cache = new BoundedGenerationCache<string, int>(maximumEntries);

        Parallel.For(0, 1000, i =>
        {
            string key = $"term-{i}";
            Assert.Equal(i, cache.GetOrAdd(key, i, static (_, value) => value));
        });

        var metrics = cache.Metrics;
        Assert.InRange(metrics.EntryCount, 0, maximumEntries);
        Assert.True(metrics.EvictionCount > 0);
    }

    [Fact]
    public void GetOrAdd_FactoryFailureDoesNotPoisonTheCache()
    {
        var cache = new BoundedGenerationCache<string, int>(4);

        Assert.Throws<InvalidOperationException>(() =>
            cache.GetOrAdd("term", 0, static (_, _) => throw new InvalidOperationException()));
        Assert.Equal(0, cache.Metrics.EntryCount);
        Assert.Equal(7, cache.GetOrAdd("term", 7, static (_, value) => value));
        Assert.Equal(1, cache.Metrics.EntryCount);
    }
}
