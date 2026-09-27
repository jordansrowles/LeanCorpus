using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class QualifiedTermCacheTests
{
    [Fact]
    public void QualifiedTermCache_BoundsEntriesAndSkipsOversizedTerms()
    {
        const int maximumEntries = 32;
        var cache = new QualifiedTermCache(maximumEntries, maximumQualifiedTermLength: 64);

        for (int i = 0; i < maximumEntries * 4; i++)
            cache.GetOrAdd($"body\0cache-term-{i}");

        var metrics = cache.Metrics;
        Assert.InRange(metrics.EntryCount, 0, maximumEntries);
        Assert.True(metrics.EvictionCount > 0);

        string recent = cache.GetOrAdd("body\0cache-term-127");
        Assert.Same(recent, cache.GetOrAdd("body\0cache-term-127"));

        string oversized = new('x', 65);
        var beforeOversizedLookup = cache.Metrics;
        cache.GetOrAdd(oversized);
        Assert.Equal(beforeOversizedLookup, cache.Metrics);
    }
}
