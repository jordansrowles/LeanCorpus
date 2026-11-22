using System.Collections.Concurrent;
using System.Globalization;
using Rowles.LeanCorpus.Tests.Core.Foundation.Unit;
using static Rowles.LeanCorpus.Tests.Core.Codecs.VectorGraphTestData;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// End-to-end tests for the opt-in Float32 vector graph (ADR052) through public writer, searcher
/// and merge APIs: route selection, exact rescoring, filters, deletes, merges and fallback.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
[Collection(LeanCorpusDefaultsCollection.Name)]
public sealed class VectorGraphEndToEndTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    private const string Field = "embedding";

    [Theory(DisplayName = "Graph: Flush Builds A Graph And Queries Use It With Recall")]
    [InlineData(true)]
    [InlineData(false)]
    public void Flush_BuildsGraph_AndQueriesUseIt(bool normalise)
    {
        var vectors = Clustered(3_000, 32, clusters: 30, spread: 0.1f, seed: 101);
        using var directory = new MMapDirectory(SubDir($"flush-{normalise}"));
        Write(directory, vectors, GraphConfig(normalise), commitEvery: vectors.Length);

        var metrics = new RouteMetrics();
        using var searcher = new IndexSearcher(directory, SearcherConfig(metrics));
        var reader = Assert.Single(searcher.GetSegmentReaders());
        var info = Assert.Single(reader.Info.VectorFields);
        Assert.True(info.HasGraph);
        Assert.False(info.HasHnsw);

        double recall = 0;
        var queries = QueriesNear(vectors, 50, seed: 102);
        foreach (var query in queries)
        {
            var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 10);
            recall += TieAwareRecall(vectors, query, hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray(), 10);
        }

        Assert.True(recall / queries.Length >= 0.95, $"Tie-aware recall@10 was {recall / queries.Length:F3}.");
        Assert.All(metrics.Routes, route => Assert.Equal(VectorSearchRoute.Graph, route));
        Assert.Equal(queries.Length, metrics.Routes.Count);
        Assert.Equal(0, metrics.FallbackSearches);
    }

    [Theory(DisplayName = "Graph: Scores Equal The Exact Route For The Same Documents")]
    [InlineData(true)]
    [InlineData(false)]
    public void GraphScores_EqualExactRouteScores(bool normalise)
    {
        var vectors = Clustered(1_200, 16, clusters: 12, spread: 0.15f, seed: 111);
        using var graphDirectory = new MMapDirectory(SubDir($"parity-graph-{normalise}"));
        using var exactDirectory = new MMapDirectory(SubDir($"parity-exact-{normalise}"));
        Write(graphDirectory, vectors, GraphConfig(normalise), commitEvery: vectors.Length);
        var exactConfig = GraphConfig(normalise);
        exactConfig.BuildHnswOnFlush = false;
        Write(exactDirectory, vectors, exactConfig, commitEvery: vectors.Length);

        using var graphSearcher = new IndexSearcher(graphDirectory, SearcherConfig());
        using var exactSearcher = new IndexSearcher(exactDirectory, SearcherConfig());
        foreach (var query in QueriesNear(vectors, 20, seed: 112))
        {
            var exact = exactSearcher.Search(new VectorQuery(Field, query, topK: 50), 50)
                .ScoreDocs.ToDictionary(hit => Ordinal(exactSearcher, hit.DocId), hit => hit.Score);
            foreach (var hit in graphSearcher.Search(new VectorQuery(Field, query, topK: 10), 10).ScoreDocs)
            {
                int ordinal = Ordinal(graphSearcher, hit.DocId);
                if (exact.TryGetValue(ordinal, out float score))
                    Assert.Equal(score, hit.Score);
            }
        }
    }

    [Fact(DisplayName = "Graph: Multiple Segments Select One Global TopK")]
    public void MultipleSegments_SelectOneGlobalTopK()
    {
        var vectors = Clustered(2_400, 24, clusters: 20, spread: 0.1f, seed: 121);
        using var directory = new MMapDirectory(SubDir("segments"));
        Write(directory, vectors, GraphConfig(normalise: true), commitEvery: 600);

        using var searcher = new IndexSearcher(directory, SearcherConfig());
        Assert.Equal(4, searcher.GetSegmentReaders().Count);
        Assert.All(searcher.GetSegmentReaders(), reader => Assert.True(reader.Info.VectorFields.Single().HasGraph));
        double recall = 0;
        var queries = QueriesNear(vectors, 30, seed: 122);
        foreach (var query in queries)
        {
            var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 100);
            Assert.Equal(10, hits.ScoreDocs.Length);
            Assert.Equal(10, hits.ScoreDocs.Select(static hit => hit.DocId).Distinct().Count());
            recall += TieAwareRecall(vectors, query, hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray(), 10);
        }
        Assert.True(recall / queries.Length >= 0.95, $"Tie-aware recall@10 was {recall / queries.Length:F3}.");
    }

    [Fact(DisplayName = "Graph: Deleted Documents Are Never Returned")]
    public void DeletedDocuments_AreNeverReturned()
    {
        var vectors = Clustered(1_500, 16, clusters: 10, spread: 0.1f, seed: 131);
        using var directory = new MMapDirectory(SubDir("deletes"));
        using (var writer = new IndexWriter(directory, GraphConfig(normalise: true)))
        {
            AddAll(writer, vectors);
            writer.Commit();
            for (int ordinal = 0; ordinal < vectors.Length; ordinal += 3)
                writer.DeleteDocuments(new TermQuery("id", $"doc-{ordinal}"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, SearcherConfig());
        foreach (var query in QueriesNear(vectors, 20, seed: 132))
        {
            var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 10);
            Assert.Equal(10, hits.ScoreDocs.Length);
            Assert.All(hits.ScoreDocs, hit => Assert.NotEqual(0, Ordinal(searcher, hit.DocId) % 3));
            double recall = TieAwareRecall(vectors, query, hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray(),
                10, ordinal => ordinal % 3 != 0);
            Assert.True(recall >= 0.8, $"Tie-aware recall@10 with deletes was {recall:F3}.");
        }
    }

    // Selectivity 0%, 0.1% and 1% leave fewer than 64 eligible documents and scan exactly. At 10%,
    // an exact scan of 400 vectors can cost less than a beam that needs about ef / 0.1 visits, so
    // the budget may send it exact. 100% must use the graph.
    [Theory(DisplayName = "Graph: Filter Selectivity Picks The Route And Stays Correct")]
    [InlineData(0, new[] { VectorSearchRoute.ExactSelectiveFilter })]
    [InlineData(1, new[] { VectorSearchRoute.ExactSelectiveFilter })]
    [InlineData(10, new[] { VectorSearchRoute.ExactSelectiveFilter })]
    [InlineData(100, new[] { VectorSearchRoute.Graph, VectorSearchRoute.ExactBudgetExceeded })]
    [InlineData(1000, new[] { VectorSearchRoute.Graph })]
    public void FilterSelectivity_PicksRouteAndStaysCorrect(int perThousand, VectorSearchRoute[] allowedRoutes)
    {
        var vectors = Clustered(4_000, 16, clusters: 20, spread: 0.1f, seed: 141);
        bool Eligible(int ordinal) => ordinal % 1000 < perThousand;
        using var directory = new MMapDirectory(SubDir($"filter-{perThousand}"));
        Write(directory, vectors, GraphConfig(normalise: true), commitEvery: vectors.Length, keep: Eligible);

        var metrics = new RouteMetrics();
        using var searcher = new IndexSearcher(directory, SearcherConfig(metrics));
        var filter = new TermQuery("bucket", "keep");
        foreach (var query in QueriesNear(vectors, 10, seed: 142))
        {
            var hits = searcher.Search(new VectorQuery(Field, query, topK: 10, filter: filter), 10);
            var ordinals = hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray();
            Assert.All(ordinals, ordinal => Assert.True(Eligible(ordinal)));
            Assert.Equal(Math.Min(10, Enumerable.Range(0, vectors.Length).Count(Eligible)), ordinals.Length);
            double recall = TieAwareRecall(vectors, query, ordinals, 10, Eligible);
            Assert.True(recall >= 0.9, $"Filtered tie-aware recall@10 at {perThousand} per thousand was {recall:F3}.");
        }

        Assert.All(metrics.Routes, route => Assert.Contains(route, allowedRoutes));
    }

    [Fact(DisplayName = "Graph: A Filter Disconnected In The Graph Stays Exact Or Correct")]
    public void DisconnectedFilter_StaysCorrect()
    {
        // The filter keeps one far cluster only; traversal from the query's cluster must not
        // invent results, and any fallback is exact.
        var vectors = Clustered(3_000, 16, clusters: 30, spread: 0.05f, seed: 151);
        using var directory = new MMapDirectory(SubDir("disconnected"));
        using (var writer = new IndexWriter(directory, GraphConfig(normalise: true)))
        {
            for (int ordinal = 0; ordinal < vectors.Length; ordinal++)
                writer.AddDocument(Document(ordinal, vectors[ordinal], keep: Cosine(vectors[ordinal], vectors[0]) > 0.95));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, SearcherConfig());
        bool Eligible(int ordinal) => Cosine(vectors[ordinal], vectors[0]) > 0.95;
        var query = Normalise(vectors.First(v => Cosine(v, vectors[0]) < 0));
        var hits = searcher.Search(new VectorQuery(Field, query, topK: 10, filter: new TermQuery("bucket", "keep")), 10);
        var ordinals = hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray();
        Assert.All(ordinals, ordinal => Assert.True(Eligible(ordinal)));
        Assert.True(TieAwareRecall(vectors, query, ordinals, 10, Eligible) >= 0.9);
    }

    [Fact(DisplayName = "Graph: Field Boosts And Non-Positive Query Boosts Use The Exact Route")]
    public void ScoreTransforms_UseExactRoute()
    {
        var vectors = Clustered(800, 8, clusters: 8, spread: 0.2f, seed: 161);
        using var directory = new MMapDirectory(SubDir("transforms"));
        using (var writer = new IndexWriter(directory, GraphConfig(normalise: true)))
        {
            for (int ordinal = 0; ordinal < vectors.Length; ordinal++)
                writer.AddDocument(Document(ordinal, vectors[ordinal], keep: true, boost: ordinal % 2 == 0 ? 1f : 3f));
            writer.Commit();
        }

        var metrics = new RouteMetrics();
        using var searcher = new IndexSearcher(directory, SearcherConfig(metrics));
        searcher.Search(new VectorQuery(Field, vectors[1], topK: 10), 10);
        Assert.Equal(VectorSearchRoute.ExactScoreTransform, Assert.Single(metrics.Routes));
    }

    [Fact(DisplayName = "Graph: Merge Rebuilds The Graph And Preserves Stored Vectors")]
    public void Merge_RebuildsGraphAndPreservesStoredVectors()
    {
        var vectors = Clustered(2_000, 16, clusters: 16, spread: 0.1f, seed: 171);
        using var directory = new MMapDirectory(SubDir("merge"));
        using (var writer = new IndexWriter(directory, GraphConfig(normalise: false)))
        {
            for (int start = 0; start < vectors.Length; start += 500)
            {
                for (int ordinal = start; ordinal < start + 500; ordinal++)
                    writer.AddDocument(Document(ordinal, vectors[ordinal], keep: true));
                writer.Commit();
            }
            writer.DeleteDocuments(new TermQuery("id", "doc-7"));
            writer.ForceMerge(1);
            writer.Commit();
        }

        var metrics = new RouteMetrics();
        using var searcher = new IndexSearcher(directory, SearcherConfig(metrics));
        var reader = Assert.Single(searcher.GetSegmentReaders());
        Assert.True(reader.Info.VectorFields.Single().HasGraph);
        Assert.Equal(vectors.Length - 1, Enumerable.Range(0, reader.MaxDoc).Count(reader.IsLive));
        for (int local = 0; local < reader.MaxDoc; local++)
        {
            if (!reader.IsLive(local)) continue;
            int ordinal = Ordinal(searcher, reader.DocBase + local);
            Assert.Equal(vectors[ordinal], reader.GetVector(Field, local));
        }

        double recall = 0;
        var queries = QueriesNear(vectors, 30, seed: 172);
        foreach (var query in queries)
        {
            var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 10);
            var ordinals = hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray();
            Assert.DoesNotContain(7, ordinals);
            recall += TieAwareRecall(vectors, query, ordinals, 10, ordinal => ordinal != 7);
        }
        Assert.True(recall / queries.Length >= 0.95, $"Post-merge tie-aware recall@10 was {recall / queries.Length:F3}.");
        Assert.All(metrics.Routes, route => Assert.Equal(VectorSearchRoute.Graph, route));
    }

    [Fact(DisplayName = "Graph: Repeated Merges Keep Recall Within The Stability Gate")]
    public void RepeatedMerges_KeepRecallWithinStabilityGate()
    {
        var vectors = Clustered(1_600, 16, clusters: 16, spread: 0.1f, seed: 181);
        var queries = QueriesNear(vectors, 40, seed: 182);
        using var directory = new MMapDirectory(SubDir("generations"));
        var recalls = new List<double>();
        using (var writer = new IndexWriter(directory, GraphConfig(normalise: true)))
        {
            AddAll(writer, vectors);
            writer.Commit();
            for (int generation = 0; generation < 4; generation++)
            {
                // Rewrite one document so each generation merges the same corpus again.
                writer.UpdateDocument("id", $"doc-{generation}", Document(generation, vectors[generation], keep: true));
                writer.Commit();
                writer.ForceMerge(1);
                writer.Commit();
                using var searcher = new IndexSearcher(directory, SearcherConfig());
                double total = 0;
                foreach (var query in queries)
                {
                    var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 10);
                    total += TieAwareRecall(vectors, query, hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray(), 10);
                }
                recalls.Add(total / queries.Length);
            }
        }

        Assert.All(recalls, recall => Assert.True(recall >= 0.95, $"Generation recalls: {string.Join(", ", recalls)}"));
        for (int i = 1; i < recalls.Count; i++)
            Assert.True(recalls[i - 1] - recalls[i] <= 0.02, $"Generation recalls: {string.Join(", ", recalls)}");
    }

    [Fact(DisplayName = "Graph: A Corrupt Graph Falls Back To The Exact Route")]
    public void CorruptGraph_FallsBackToExactRoute()
    {
        var vectors = Clustered(600, 8, clusters: 6, spread: 0.2f, seed: 191);
        string path = SubDir("corrupt");
        using (var directory = new MMapDirectory(path))
            Write(directory, vectors, GraphConfig(normalise: true), commitEvery: vectors.Length);

        string graphFile = Directory.EnumerateFiles(path, "*.vgr").Single();
        byte[] bytes = File.ReadAllBytes(graphFile);
        bytes[bytes.Length / 2] ^= 0x33;
        File.WriteAllBytes(graphFile, bytes);

        using var reopened = new MMapDirectory(path);
        var metrics = new RouteMetrics();
        using var searcher = new IndexSearcher(reopened, SearcherConfig(metrics));
        var query = Normalise(vectors[5]);
        var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 10);
        Assert.Equal(1.0, TieAwareRecall(vectors, query, hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray(), 10));
        Assert.Equal(VectorSearchRoute.ExactGraphUnavailable, Assert.Single(metrics.Routes));

        var check = IndexValidator.Check(reopened, new IndexCheckOptions { Deep = true });
        Assert.Contains(check.DetailedIssues, issue => issue.Code == IndexCheckIssueCodes.VectorGraphReadFailure);
    }

    [Fact(DisplayName = "Graph: Explain Reports The Route")]
    public void Explain_ReportsTheRoute()
    {
        var vectors = Clustered(1_000, 8, clusters: 8, spread: 0.1f, seed: 201);
        using var directory = new MMapDirectory(SubDir("explain"));
        Write(directory, vectors, GraphConfig(normalise: true), commitEvery: vectors.Length);

        using var searcher = new IndexSearcher(directory, SearcherConfig());
        var query = new VectorQuery(Field, vectors[3], topK: 5);
        var top = searcher.Search(query, 5).ScoreDocs[0];
        var explanation = searcher.Explain(query, top.DocId);
        Assert.NotNull(explanation);
        Assert.Contains("Float32 vector graph", explanation!.Description, StringComparison.Ordinal);
        Assert.Contains(explanation.Details, detail => detail.Description == "vectorSearchRoute=Graph");
        Assert.Equal(top.Score, explanation.Score);
    }

    [Fact(DisplayName = "Graph: Compound Segments And Concurrent Readers Search The Graph")]
    public void CompoundSegmentsAndConcurrentReaders_SearchTheGraph()
    {
        var vectors = Clustered(1_500, 16, clusters: 12, spread: 0.1f, seed: 211);
        using var directory = new MMapDirectory(SubDir("compound"));
        var config = GraphConfig(normalise: true);
        config.UseCompoundFile = true;
        Write(directory, vectors, config, commitEvery: 750);

        var metrics = new RouteMetrics();
        using var searcher = new IndexSearcher(directory, SearcherConfig(metrics));
        Assert.All(searcher.GetSegmentReaders(), reader => Assert.True(reader.Info.IsCompoundFile));
        var queries = QueriesNear(vectors, 32, seed: 212);
        var recalls = new ConcurrentBag<double>();
        Parallel.ForEach(queries, new ParallelOptions { MaxDegreeOfParallelism = 4 }, query =>
        {
            var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 10);
            recalls.Add(TieAwareRecall(vectors, query, hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray(), 10));
        });

        Assert.True(recalls.Average() >= 0.95, $"Tie-aware recall@10 was {recalls.Average():F3}.");
        Assert.All(metrics.Routes, route => Assert.Equal(VectorSearchRoute.Graph, route));
    }

    [Fact(DisplayName = "Graph: Compound Merge, Backup And Restore Keep A Usable Graph")]
    public void CompoundMergeBackupAndRestore_KeepAUsableGraph()
    {
        var vectors = Clustered(1_200, 16, clusters: 12, spread: 0.1f, seed: 231);
        string source = SubDir("backup-source");
        var config = GraphConfig(normalise: true);
        config.UseCompoundFile = true;
        using (var directory = new MMapDirectory(source))
        using (var writer = new IndexWriter(directory, config))
        {
            for (int start = 0; start < vectors.Length; start += 400)
            {
                for (int ordinal = start; ordinal < start + 400; ordinal++)
                    writer.AddDocument(Document(ordinal, vectors[ordinal], keep: true));
                writer.Commit();
            }
            writer.ForceMerge(1);
            writer.Commit();
        }

        string backup = Path.Combine(fixture.Path, "vector-graph-backup-copy");
        string restored = Path.Combine(fixture.Path, "vector-graph-backup-restored");
        IndexBackup.Backup(source, backup);
        IndexBackup.Restore(backup, restored);

        using var restoredDirectory = new MMapDirectory(restored);
        Assert.True(IndexValidator.Check(restoredDirectory, new IndexCheckOptions { Deep = true }).IsHealthy);
        var metrics = new RouteMetrics();
        using var searcher = new IndexSearcher(restoredDirectory, SearcherConfig(metrics));
        var reader = Assert.Single(searcher.GetSegmentReaders());
        Assert.True(reader.Info.IsCompoundFile);
        Assert.True(reader.Info.VectorFields.Single().HasGraph);

        double recall = 0;
        var queries = QueriesNear(vectors, 20, seed: 232);
        foreach (var query in queries)
        {
            var hits = searcher.Search(new VectorQuery(Field, query, topK: 10), 10);
            recall += TieAwareRecall(vectors, query, hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray(), 10);
        }
        Assert.True(recall / queries.Length >= 0.95, $"Tie-aware recall@10 was {recall / queries.Length:F3}.");
        Assert.All(metrics.Routes, route => Assert.Equal(VectorSearchRoute.Graph, route));
    }

    [Fact(DisplayName = "Graph: A Missing Declared Graph Is Reported Once As Missing")]
    public void MissingDeclaredGraph_IsReportedOnceAsMissing()
    {
        var vectors = Clustered(300, 8, clusters: 4, spread: 0.2f, seed: 241);
        string path = SubDir("missing");
        using (var directory = new MMapDirectory(path))
            Write(directory, vectors, GraphConfig(normalise: true), commitEvery: vectors.Length);
        File.Delete(Directory.EnumerateFiles(path, "*.vgr").Single());

        using var reopened = new MMapDirectory(path);
        var check = IndexValidator.Check(reopened, new IndexCheckOptions { Deep = true });
        Assert.Single(check.DetailedIssues, issue => issue.Code == IndexCheckIssueCodes.VectorGraphFileMissing);
        Assert.DoesNotContain(check.DetailedIssues, issue => issue.Code == IndexCheckIssueCodes.VectorGraphReadFailure);
        Assert.NotEmpty(IndexRepairRecommendations.ForIssue(IndexCheckIssueCodes.VectorGraphFileMissing));
    }

    [Fact(DisplayName = "Graph: A Null Build Config Is Rejected Before Side Effects")]
    public void NullBuildConfig_IsRejectedBeforeSideEffects()
    {
        string path = SubDir("null-config");
        using var directory = new MMapDirectory(path);
        Assert.Throws<ArgumentException>(() => new IndexWriter(directory,
            new IndexWriterConfig { BuildHnswOnFlush = true, HnswBuildConfig = null! }));
        Assert.Empty(Directory.EnumerateFileSystemEntries(path));
    }

    [Fact(DisplayName = "Graph: Built-In Metrics Count Graph Routes And Builds")]
    public void BuiltInMetrics_CountGraphRoutesAndBuilds()
    {
        var vectors = Clustered(1_000, 8, clusters: 8, spread: 0.1f, seed: 251);
        var metrics = new DefaultMetricsCollector();
        using var directory = new MMapDirectory(SubDir("default-metrics"));
        var config = GraphConfig(normalise: true);
        config.Metrics = metrics;
        using (var writer = new IndexWriter(directory, config))
        {
            for (int start = 0; start < vectors.Length; start += 500)
            {
                for (int ordinal = start; ordinal < start + 500; ordinal++)
                    writer.AddDocument(Document(ordinal, vectors[ordinal], keep: true));
                writer.Commit();
            }
            writer.ForceMerge(1);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, SearcherConfig(metrics));
        searcher.Search(new VectorQuery(Field, vectors[0], topK: 5), 5);
        var snapshot = metrics.GetSnapshot();
        Assert.Equal(1, snapshot.VectorGraphRouteCount);
        Assert.Equal(0, snapshot.VectorGraphFallbackCount);
        Assert.Equal(0, snapshot.HnswVectorsScanned);
        Assert.Equal(3, snapshot.HnswBuildCount);
    }

    [Fact(DisplayName = "Graph: Sparse Presence And Tiny Segments Stay Correct")]
    public void SparsePresenceAndTinySegments_StayCorrect()
    {
        var vectors = Clustered(900, 8, clusters: 6, spread: 0.2f, seed: 221);
        using var directory = new MMapDirectory(SubDir("sparse"));
        using (var writer = new IndexWriter(directory, GraphConfig(normalise: true)))
        {
            for (int ordinal = 0; ordinal < vectors.Length; ordinal++)
                writer.AddDocument(Document(ordinal, ordinal % 5 == 0 ? null : vectors[ordinal], keep: true));
            writer.Commit();
            writer.AddDocument(Document(vectors.Length, vectors[1], keep: true));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, SearcherConfig());
        Assert.False(searcher.GetSegmentReaders()[1].Info.VectorFields.Single().HasGraph);
        var hits = searcher.Search(new VectorQuery(Field, vectors[1], topK: 5), 5);
        var ordinals = hits.ScoreDocs.Select(hit => Ordinal(searcher, hit.DocId)).ToArray();
        Assert.Contains(vectors.Length, ordinals);
        Assert.DoesNotContain(ordinals, ordinal => ordinal < vectors.Length && ordinal % 5 == 0);
    }

    private static IndexWriterConfig GraphConfig(bool normalise)
        => new()
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = normalise,
            HnswSeed = 42,
            MergeThreshold = 100,
            MergeThrottleSegments = 100,
            MaxBufferedDocs = 100_000,
        };

    private static IndexSearcherConfig SearcherConfig(IMetricsCollector? metrics = null)
        => new()
        {
            ParallelSearch = false,
            EnableQueryCache = false,
            Metrics = metrics ?? NullMetricsCollector.Instance,
        };

    private static void Write(
        MMapDirectory directory, float[][] vectors, IndexWriterConfig config, int commitEvery, Func<int, bool>? keep = null)
    {
        using var writer = new IndexWriter(directory, config);
        for (int ordinal = 0; ordinal < vectors.Length; ordinal++)
        {
            writer.AddDocument(Document(ordinal, vectors[ordinal], keep: keep?.Invoke(ordinal) ?? true));
            if ((ordinal + 1) % commitEvery == 0)
                writer.Commit();
        }
        writer.Commit();
    }

    private static void AddAll(IndexWriter writer, float[][] vectors)
    {
        for (int ordinal = 0; ordinal < vectors.Length; ordinal++)
            writer.AddDocument(Document(ordinal, vectors[ordinal], keep: true));
    }

    private static LeanDocument Document(int ordinal, float[]? vector, bool keep, float boost = 1f)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", $"doc-{ordinal}"));
        document.Add(new StoredField("ordinal", ordinal));
        document.Add(new StringField("bucket", keep ? "keep" : "drop"));
        if (vector is not null)
            document.Add(new VectorField(Field, vector, boost));
        return document;
    }

    private static int Ordinal(IndexSearcher searcher, int docId)
        => int.Parse(searcher.GetStoredFields(docId)["ordinal"].Single(), CultureInfo.InvariantCulture);

    private static float[][] QueriesNear(float[][] vectors, int count, int seed)
    {
        var random = new Random(seed); // NOSONAR S2245: deterministic test data
        return Enumerable.Range(0, count).Select(i =>
        {
            float[] anchor = vectors[random.Next(vectors.Length)];
            float[] noise = Gaussian(random, anchor.Length, 0.05f);
            return anchor.Zip(noise, static (a, n) => a + n).ToArray();
        }).ToArray();
    }

    private string SubDir(string name)
    {
        string path = Path.Combine(fixture.Path, "vector-graph-" + name);
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RouteMetrics : IMetricsCollector
    {
        private readonly ConcurrentQueue<VectorSearchRoute> _routes = new();
        private int _fallbackSearches;

        public IReadOnlyCollection<VectorSearchRoute> Routes => _routes.ToArray();
        public int FallbackSearches => Volatile.Read(ref _fallbackSearches);

        public void RecordSearchLatency(TimeSpan elapsed) { }
        public void RecordCacheHit() { }
        public void RecordCacheMiss() { }
        public void RecordFlush(TimeSpan elapsed) { }
        public void RecordMerge(TimeSpan elapsed, int segmentsMerged) { }
        public void RecordCommit(TimeSpan elapsed) { }
        public void RecordVectorSearchRoute(VectorSearchRoute route) => _routes.Enqueue(route);

        public void RecordHnswSearch(TimeSpan elapsed, int nodesVisited, int scannedVectors, bool usedExhaustiveFallback)
        {
            if (usedExhaustiveFallback)
                Interlocked.Increment(ref _fallbackSearches);
        }

        public MetricsSnapshot GetSnapshot() => MetricsSnapshot.Empty;
    }
}
