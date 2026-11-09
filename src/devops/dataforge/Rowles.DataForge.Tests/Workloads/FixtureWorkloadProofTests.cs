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

    [Fact]
    public void Function_score_gate_rejects_a_wrong_mode_or_similarity()
    {
        (string Id, float Score)[] baseScores = [("0", 2.0f), ("1", 1.0f), ("2", 0.5f)];
        var prices = new Dictionary<string, double>(StringComparer.Ordinal) { ["0"] = 10, ["1"] = 15, ["2"] = 100 };

        // Multiply: 20, 15, 50.
        FunctionScoreQueryBenchmarks.ValidateModeTopK("LeanCorpus", "Multiply", baseScores, prices,
            [("2", 50f), ("0", 20f), ("1", 15f)], 3);

        // The timed query applied Replace instead of Multiply.
        Assert.Throws<InvalidOperationException>(() => FunctionScoreQueryBenchmarks.ValidateModeTopK("LeanCorpus", "Multiply", baseScores, prices,
            [("2", 100f), ("1", 15f), ("0", 10f)], 3));

        // The timed searcher used a different similarity, so base scores were halved.
        Assert.Throws<InvalidOperationException>(() => FunctionScoreQueryBenchmarks.ValidateModeTopK("Lucene.NET matched BM25", "Multiply", baseScores, prices,
            [("2", 25f), ("0", 10f), ("1", 7.5f)], 3));

        // The timed query matched nothing.
        Assert.Throws<InvalidOperationException>(() => FunctionScoreQueryBenchmarks.ValidateModeTopK("LeanCorpus", "Multiply", [], prices, [], 0));
    }

    [Fact]
    public void Block_join_gate_rejects_duplicate_child_and_wrong_parents()
    {
        string[] expected = ["p0", "p2"];
        BlockJoinIndexBenchmarks.ValidateParents("LeanCorpus", expected, ["p2", "p0"], 2);

        Assert.Throws<InvalidOperationException>(() => BlockJoinIndexBenchmarks.ValidateParents("LeanCorpus", expected, ["p0", "p0"], 2));
        Assert.Throws<InvalidOperationException>(() => BlockJoinIndexBenchmarks.ValidateParents("LeanCorpus", expected, ["p0", "c2-1"], 2));
        Assert.Throws<InvalidOperationException>(() => BlockJoinIndexBenchmarks.ValidateParents("LeanCorpus", expected, ["p0", "p1"], 2));
        Assert.Throws<InvalidOperationException>(() => BlockJoinIndexBenchmarks.ValidateParents("LeanCorpus", expected, ["p0", "p2"], 3));
    }

    [Theory]
    [InlineData(1_000)]
    [InlineData(3)]
    public void Block_join_returns_the_derived_parents_on_both_engines(int maxBufferedDocs)
    {
        (string ParentTitle, string[] ChildBodies)[] blocks =
        [
            ("no match", ["alpha beta", "gamma", "delta"]),
            ("one match", ["alpha", "said beta", "gamma"]),
            ("many matches", ["said", "said alpha", "beta said"]),
            ("deleted child", ["alpha", "said only", "beta"]),
            ("deleted parent", ["said", "alpha", "beta"]),
            ("partly deleted", ["said one", "said two", "gamma"])
        ];
        var deleted = new HashSet<string>(StringComparer.Ordinal)
        {
            BlockJoinIndexBenchmarks.ChildId(3, 1),
            BlockJoinIndexBenchmarks.ParentId(4),
            BlockJoinIndexBenchmarks.ChildId(5, 0)
        };
        string[] expected = BlockJoinIndexBenchmarks.ExpectedParents(blocks, "said", deleted);
        Assert.Equal(["p1", "p2", "p5"], expected);

        string root = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("dataforge"), "leancorpus-blockjoin-proof-" + Guid.NewGuid().ToString("N"));
        try
        {
            string leanPath = Path.Combine(root, "lean");
            Directory.CreateDirectory(leanPath);
            using (var directory = new LeanMMapDirectory(leanPath))
            {
                using (var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig { MaxBufferedDocs = maxBufferedDocs, RamBufferSizeMB = 64 }))
                {
                    BlockJoinIndexBenchmarks.IndexLeanBlocks(writer, blocks);
                    writer.Commit();
                    foreach (var id in deleted)
                        writer.DeleteDocuments(new LeanTermQuery("id", id));
                    writer.Commit();
                }

                using var searcher = new LeanIndexSearcher(directory);
                var hits = searcher.Search(new LeanBlockJoinQuery(new LeanTermQuery("body", "said")), 100);
                BlockJoinIndexBenchmarks.ValidateParents("LeanCorpus", expected,
                    hits.ScoreDocs.Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0]).ToArray(), hits.TotalHits);
            }

            string lucenePath = Path.Combine(root, "lucene");
            Directory.CreateDirectory(lucenePath);
            using (var directory = new Lucene.Net.Store.MMapDirectory(new DirectoryInfo(lucenePath)))
            {
                using (var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48))
                using (var writer = new IndexWriter(directory, new IndexWriterConfig(LuceneVersion.LUCENE_48, analyser) { MaxBufferedDocs = Math.Max(2, maxBufferedDocs) }))
                {
                    BlockJoinIndexBenchmarks.IndexLuceneBlocks(writer, blocks);
                    writer.Commit();
                    foreach (var id in deleted)
                        writer.DeleteDocuments(new Term("id", id));
                    writer.Commit();
                }

                using var reader = DirectoryReader.Open(directory);
                var searcher = new IndexSearcher(reader);
                var parents = new FixedBitSetCachingWrapperFilter(new QueryWrapperFilter(new TermQuery(new Term("type", "parent"))));
                var hits = searcher.Search(new ToParentBlockJoinQuery(new TermQuery(new Term("body", "said")), parents, ScoreMode.None), 100);
                BlockJoinIndexBenchmarks.ValidateParents("Lucene.NET", expected,
                    hits.ScoreDocs.Select(hit => reader.Document(hit.Doc).Get("id")).ToArray(), hits.TotalHits);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Deletion_verification_fails_when_the_timed_commit_is_suppressed()
    {
        string[] bodies = Enumerable.Range(0, 50).Select(static i => "body " + i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        string root = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("dataforge"), "leancorpus-deletion-proof-" + Guid.NewGuid().ToString("N"));
        string leanPath = Path.Combine(root, "lean");
        string lucenePath = Path.Combine(root, "lucene");
        Directory.CreateDirectory(leanPath);
        Directory.CreateDirectory(lucenePath);
        try
        {
            using var leanDirectory = new LeanMMapDirectory(leanPath);
            using var leanWriter = new LeanIndexWriter(leanDirectory, DeletionQueueBenchmarks.CreateLeanWriterConfig());
            DeletionBenchmarkFixture.AddLeanDocuments(leanWriter, bodies);
            leanWriter.Commit();

            using var luceneDirectory = new Lucene.Net.Store.MMapDirectory(new DirectoryInfo(lucenePath));
            using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            using var luceneWriter = new IndexWriter(luceneDirectory, DeletionBenchmarkFixture.CreateLuceneWriterConfig(analyser));
            DeletionBenchmarkFixture.AddLuceneDocuments(luceneWriter, bodies);
            luceneWriter.Commit();

            using var leanPreDeleteDirectory = new LeanMMapDirectory(leanPath);
            using var leanPreDelete = new LeanIndexSearcher(leanPreDeleteDirectory);
            using var lucenePreDelete = DirectoryReader.Open(luceneDirectory);

            DeletionBenchmarkFixture.QueueLeanDeletes(leanWriter, 5);
            DeletionBenchmarkFixture.QueueLuceneDeletes(luceneWriter, 5);
            string[] survivors = DeletionBenchmarkFixture.GetSurvivingIds(bodies.Length, 5);

            // The timed commit is suppressed: verification at the commit boundary must fail
            // before any cleanup commit can repair the state.
            Assert.Throws<InvalidOperationException>(() => DeletionBenchmarkFixture.VerifyLeanIds(leanPath, survivors));
            Assert.Throws<InvalidOperationException>(() => DeletionBenchmarkFixture.VerifyLuceneIds(lucenePath, survivors));
            DeletionBenchmarkFixture.VerifyLeanIds(leanPath, DeletionBenchmarkFixture.GetSurvivingIds(bodies.Length, 0));
            DeletionBenchmarkFixture.VerifyLuceneIds(lucenePath, DeletionBenchmarkFixture.GetSurvivingIds(bodies.Length, 0));

            // The timed commit runs: survivors are visible with the writers still open, and the
            // pre-delete readers still see every original.
            leanWriter.Commit();
            luceneWriter.Commit();
            DeletionBenchmarkFixture.VerifyLeanIds(leanPath, survivors);
            DeletionBenchmarkFixture.VerifyLuceneIds(lucenePath, survivors);
            DeletionBenchmarkFixture.VerifyPointInTime(bodies.Length, leanPreDelete, lucenePreDelete);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
