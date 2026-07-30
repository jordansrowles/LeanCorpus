using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// Contains unit tests for HNSW Filtered.
/// </summary>
[Trait("Category", "Phase3")]
public sealed class HnswFilteredTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HnswFilteredTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static float[][] BuildRandomVectors(int count, int dim, int seed)
    {
        var rnd = new Random(seed);
        var vectors = new float[count][];
        for (int i = 0; i < count; i++)
        {
            var v = new float[dim];
            for (int d = 0; d < dim; d++)
                v[d] = (float)(rnd.NextDouble() * 2 - 1);
            vectors[i] = v;
        }
        return vectors;
    }

    private static (MMapDirectory dir, float[][] vectors) BuildIndex(string subDir, int n, int dim, IndexWriterConfig cfg)
    {
        var dir = new MMapDirectory(subDir);
        var vectors = BuildRandomVectors(n, dim, seed: 42);
        using var writer = new IndexWriter(dir, cfg);
        for (int i = 0; i < n; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(vectors[i])));
            doc.Add(new TextField("colour", (i % 3) switch { 0 => "red", 1 => "green", _ => "blue" }));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return (dir, vectors);
    }

    private static LeanDocument CreateFilteredVectorDocument(
        float[] vector,
        string cohort,
        string bucket)
    {
        var document = new LeanDocument();
        document.Add(new VectorField("emb", vector));
        document.Add(new TextField("cohort", cohort));
        document.Add(new TextField("bucket", bucket));
        return document;
    }

    private static int[] SearchDocumentIds(
        MMapDirectory directory,
        float[] query,
        Query filter,
        int topK)
    {
        using var searcher = new IndexSearcher(directory);
        return searcher.Search(new VectorQuery(
                "emb",
                query,
                topK,
                efSearch: 256,
                oversamplingFactor: 4,
                filter: filter),
            topK).ScoreDocs.Select(static hit => hit.DocId).ToArray();
    }

    /// <summary>
    /// Verifies the Filter: Restricts Results To Matching Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Filter: Restricts Results To Matching Docs")]
    public void Filter_RestrictsResultsToMatchingDocs()
    {
        var cfg = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1L,
        };
        var (dir, _) = BuildIndex(SubDir("hnsw_filter_basic"), n: 90, dim: 16, cfg);

        using var searcher = new IndexSearcher(dir);
        var query = new VectorQuery(
            "emb",
            BuildRandomVectors(1, 16, 100)[0],
            topK: 10,
            efSearch: 64,
            filter: new TermQuery("colour", "red"));

        var results = searcher.Search(query, 10);
        Assert.True(results.TotalHits > 0);
        // All returned docs must match the filter (red, indices % 3 == 0).
        foreach (var sd in results.ScoreDocs)
            Assert.Equal(0, sd.DocId % 3);
    }

    /// <summary>
    /// Verifies the Filter: Highly Selective Brute Force Still Returns Top K scenario.
    /// </summary>
    [Fact(DisplayName = "Filter: Highly Selective Brute Force Still Returns Top K")]
    public void Filter_HighlySelective_BruteForceStillReturnsTopK()
    {
        var cfg = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 2L,
        };
        // 60 docs: only 2 will match a unique tag — falls into brute-force selectivity bucket.
        var dir = new MMapDirectory(SubDir("hnsw_filter_selective"));
        var vecs = BuildRandomVectors(60, 16, seed: 7);
        using (var writer = new IndexWriter(dir, cfg))
        {
            for (int i = 0; i < 60; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(vecs[i])));
                doc.Add(new TextField("tag", i is 5 or 42 ? "rare" : "common"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new VectorQuery(
            "emb",
            vecs[5], // doc 5 should be the closest match (cos = 1)
            topK: 5,
            filter: new TermQuery("tag", "rare"));

        var results = searcher.Search(query, 5);
        Assert.True(results.TotalHits > 0);
        Assert.True(results.TotalHits <= 2);
        Assert.Equal(5, results.ScoreDocs[0].DocId);
    }

    [Fact(DisplayName = "Filter: Moderately selective planner prefers calibrated exact scan")]
    public void Filter_ModeratelySelectivePlannerPrefersCalibratedExactScan()
    {
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            MaxBufferedDocs = 1_000,
            HnswSeed = 3L,
        };
        var (directory, vectors) = BuildIndex(
            SubDir("hnsw_filter_calibrated_exact"),
            n: 1_000,
            dim: 16,
            config);
        using (directory)
        {
            using var searcher = new IndexSearcher(directory);
            var query = new VectorQuery(
                "emb",
                vectors[0],
                topK: 10,
                efSearch: 64,
                filter: new TermQuery("colour", "red"));

            var explanation = searcher.Explain(query, globalDocId: 0);

            Assert.NotNull(explanation);
            Assert.Contains("exact filtered scan", explanation.Description, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Verifies the Filter: No Matches Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Filter: No Matches Returns Empty")]
    public void Filter_NoMatches_ReturnsEmpty()
    {
        var cfg = new IndexWriterConfig { BuildHnswOnFlush = false };
        var (dir, _) = BuildIndex(SubDir("hnsw_filter_empty"), n: 30, dim: 8, cfg);

        using var searcher = new IndexSearcher(dir);
        var query = new VectorQuery(
            "emb",
            BuildRandomVectors(1, 8, 0)[0],
            topK: 5,
            filter: new TermQuery("colour", "magenta"));

        var results = searcher.Search(query, 5);
        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Characterises filtered HNSW recall against the exact flat reference for deliberately
    /// different filter shapes. This guards traversal through rejected bridges, sparse
    /// allow-lists, deleted documents, and post-merge graph remapping together.
    /// </summary>
    [Fact(DisplayName = "Filter: HNSW Matches Exact Reference Across Adversarial Shapes")]
    public void Filter_HnswMatchesExactReferenceAcrossAdversarialShapes()
    {
        const int count = 180;
        const int dimension = 12;
        var vectors = BuildRandomVectors(count, dimension, seed: 891);
        var hnswDirectory = new MMapDirectory(SubDir("hnsw_filter_adversarial_hnsw"));
        var exactDirectory = new MMapDirectory(SubDir("hnsw_filter_adversarial_exact"));
        var hnswConfig = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswSeed = 17,
            HnswBuildConfig = new HnswBuildConfig { M = 24, M0 = 48, EfConstruction = 200 },
        };
        var exactConfig = new IndexWriterConfig
        {
            BuildHnswOnFlush = false,
            NormaliseVectors = true,
        };

        using (var hnswWriter = new IndexWriter(hnswDirectory, hnswConfig))
        using (var exactWriter = new IndexWriter(exactDirectory, exactConfig))
        {
            for (int docId = 0; docId < count; docId++)
            {
                // "clustered" groups nearest vectors into the same filter, while
                // "anti" places the nearest document in the opposite cohort. The
                // sparse bucket intentionally has only a handful of candidates.
                string cohort = docId < 90 ? "clustered" : "anti";
                string bucket = docId is 3 or 27 or 89 or 143 or 177 ? "sparse" : "dense";
                hnswWriter.AddDocument(CreateFilteredVectorDocument(vectors[docId], cohort, bucket));
                exactWriter.AddDocument(CreateFilteredVectorDocument(vectors[docId], cohort, bucket));
            }
            hnswWriter.Commit();
            exactWriter.Commit();
        }

        // Delete the same selected documents in both indices and force a merge so the
        // comparison also covers live-doc handling and graph/vector ordinal remapping.
        using (var hnswWriter = new IndexWriter(hnswDirectory, hnswConfig))
        using (var exactWriter = new IndexWriter(exactDirectory, exactConfig))
        {
            hnswWriter.DeleteDocuments(new TermQuery("bucket", "dense"));
            exactWriter.DeleteDocuments(new TermQuery("bucket", "dense"));
            hnswWriter.Commit();
            exactWriter.Commit();
        }

        // The dense delete leaves the sparse set, exercising a highly selective and
        // deleted-document path. The other filters are checked before a destructive
        // deletion in dedicated, freshly indexed directories below.
        var sparse = new TermQuery("bucket", "sparse");
        Assert.Equal(
            SearchDocumentIds(exactDirectory, vectors[3], sparse, topK: 5),
            SearchDocumentIds(hnswDirectory, vectors[3], sparse, topK: 5));

        hnswDirectory.Dispose();
        exactDirectory.Dispose();

        // Random, clustered, anti-correlated, and merged-segment filters run against
        // a second pair without the deletion, retaining enough candidates to make the
        // HNSW comparison meaningful rather than forcing the exact-scan planner.
        var hnsw = new MMapDirectory(SubDir("hnsw_filter_recall_hnsw"));
        var exact = new MMapDirectory(SubDir("hnsw_filter_recall_exact"));
        using (var hnswWriter = new IndexWriter(hnsw, hnswConfig))
        using (var exactWriter = new IndexWriter(exact, exactConfig))
        {
            for (int docId = 0; docId < count; docId++)
            {
                string cohort = docId < 90 ? "clustered" : "anti";
                string bucket = docId % 2 == 0 ? "even" : "odd";
                hnswWriter.AddDocument(CreateFilteredVectorDocument(vectors[docId], cohort, bucket));
                exactWriter.AddDocument(CreateFilteredVectorDocument(vectors[docId], cohort, bucket));
                if (docId == 89)
                {
                    hnswWriter.Commit();
                    exactWriter.Commit();
                }
            }
            hnswWriter.Commit();
            exactWriter.Commit();
            hnswWriter.ForceMerge(1);
            exactWriter.ForceMerge(1);
        }

        Query[] filters =
        [
            new TermQuery("bucket", "even"),
            new TermQuery("cohort", "clustered"),
            new TermQuery("cohort", "anti"),
        ];
        foreach (Query filter in filters)
        {
            int[] expected = SearchDocumentIds(exact, vectors[21], filter, topK: 10);
            int[] actual = SearchDocumentIds(hnsw, vectors[21], filter, topK: 10);
            Assert.Equal(expected, actual);
        }
    }
}
