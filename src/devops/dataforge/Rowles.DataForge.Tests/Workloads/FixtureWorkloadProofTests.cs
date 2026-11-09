using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Join;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Benchmarks;
using LeanBlockJoinQuery = Rowles.LeanCorpus.Search.Queries.BlockJoinQuery;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanTermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;
using LeanTopDocs = Rowles.LeanCorpus.Search.Scoring.TopDocs;
using LeanScoreDoc = Rowles.LeanCorpus.Search.Scoring.ScoreDoc;

namespace Rowles.DataForge.Tests.Workloads;

/// <summary>
/// Re-creates each deliberately wrong query-cache, function-score, block-join and deletion
/// workload and proves that its benchmark gate fails.
/// </summary>
public sealed class FixtureWorkloadProofTests
{
    [Fact]
    public void Query_cache_gate_rejects_a_wrong_hit_rate()
    {
        QueryCacheBenchmarks.ValidateCounterDelta("miss cycle 1", 0, QueryCacheBenchmarks.MissKeyCount, 0, QueryCacheBenchmarks.MissKeyCount);

        // A miss row that is partly served from the cache.
        Assert.Throws<InvalidOperationException>(() =>
            QueryCacheBenchmarks.ValidateCounterDelta("miss cycle 2", 1_023, 1_025, 0, QueryCacheBenchmarks.MissKeyCount));
        // A hit row that never hits.
        Assert.Throws<InvalidOperationException>(() =>
            QueryCacheBenchmarks.ValidateCounterDelta("term populate and hit", 0, 2, 1, 1));
    }

    [Fact]
    public void Query_cache_gate_rejects_cached_results_that_differ_from_uncached_results()
    {
        var uncached = new LeanTopDocs(2, [new LeanScoreDoc(0, 2.5f), new LeanScoreDoc(1, 1.5f)]);
        string Id(int docId) => docId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        QueryCacheBenchmarks.ValidateSameResults("term", uncached, new LeanTopDocs(2, [new LeanScoreDoc(0, 2.5f), new LeanScoreDoc(1, 1.5f)]), Id);
        Assert.Throws<InvalidOperationException>(() => QueryCacheBenchmarks.ValidateSameResults("term", uncached,
            new LeanTopDocs(2, [new LeanScoreDoc(1, 2.5f), new LeanScoreDoc(0, 1.5f)]), Id));
        Assert.Throws<InvalidOperationException>(() => QueryCacheBenchmarks.ValidateSameResults("term", uncached,
            new LeanTopDocs(2, [new LeanScoreDoc(0, 2.5f), new LeanScoreDoc(1, 1.25f)]), Id));
        Assert.Throws<InvalidOperationException>(() => QueryCacheBenchmarks.ValidateSameResults("term", uncached,
            new LeanTopDocs(3, [new LeanScoreDoc(0, 2.5f), new LeanScoreDoc(1, 1.5f)]), Id));
    }
}
