using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanVectorQuery = Rowles.LeanCorpus.Search.Queries.VectorQuery;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;

namespace Rowles.LeanCorpus.Benchmarks;

internal sealed record HnswGraphQualityMeasurement(
    string QueryId,
    int TopK,
    int ExactRepresentationCount,
    int GraphReturnedCount,
    double GraphRecallAgainstExactRepresentation);

internal sealed record HnswSearchBudgetMeasurement(
    int TopK,
    int ResolvedEfSearch,
    int EffectiveEfSearch,
    int ShortlistSize);

/// <summary>
/// Measures HNSW search against an exact flat reference over the same vectors.
/// The reference row is deliberately scalar because Lucene.NET 4.8 has no
/// native vector or HNSW search API.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(2)]
[IterationCount(5)]
public class HnswSearchBenchmarks
{
    internal const int GraphM = 16;
    internal const int GraphM0 = 32;
    internal const int GraphEfConstruction = 100;
    internal const long GraphSeed = 1L;

    internal static HnswBuildConfig CreateGraphBuildConfig() => new()
    {
        M = GraphM,
        M0 = GraphM0,
        EfConstruction = GraphEfConstruction,
    };

    internal static LeanVectorQuery CreateQualificationQuery(string field, float[] queryVector, int topK)
        => new(field, queryVector, topK: topK);

    internal static HnswSearchBudgetMeasurement[] CreateQualificationBudgets(int availableCandidates)
        => [.. new[] { 1, 10, 100 }.Select(topK =>
        {
            LeanVectorQuery query = CreateQualificationQuery("emb", new float[64], topK);
            return new HnswSearchBudgetMeasurement(
                topK,
                query.EfSearch,
                query.GetEffectiveEfSearch(availableCandidates),
                query.GetShortlistSize(availableCandidates));
        })];

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(10_000, 100_000);

    [ParamsSource(nameof(DocCounts))]
    public int DocCount { get; set; }

    [Params(32, 64, 128, 384, 768)]
    public int Dimension { get; set; }

    [Params(VectorQuantisation.None, VectorQuantisation.Int8)]
    public VectorQuantisation Quantisation { get; set; }

    [Params(VectorDistribution.Uniform, VectorDistribution.Clustered, VectorDistribution.QuantisationHostile)]
    public VectorDistribution Distribution { get; set; }

    [Params(42, 2025)]
    public int QuerySeed { get; set; }

    private static readonly System.Threading.Lock s_gate = new();
    private static string s_lastKey = string.Empty;
    private static bool s_built;
    private static string s_hnswPath = string.Empty;
    private static string s_flatPath = string.Empty;
    private static LeanIndexSearcher s_hnswSearcher = default!;
    private static LeanIndexSearcher s_flatSearcher = default!;
    private static DefaultMetricsCollector s_hnswMetrics = new();
    private static VectorRecord[] s_referenceRecords = [];

    private float[] _query = [];
    private VectorQueryCase _queryCase = default!;
    private BenchmarkVectorDataset _dataset = default!;
    private int[] _flatTopDocumentIds = [];
    private int _scalarChecksum;
    private int _hnswChecksum;

    [GlobalSetup]
    public void Setup()
    {
        LeanCorpusDefaults.Reset();
        BenchmarkVectorData.ValidateSupportedQuantisation(Quantisation);
        var setupStopwatch = Stopwatch.StartNew();
        BenchmarkVectorQuality.ValidateFixture();
        _dataset = BenchmarkVectorData.Get(DocCount, Dimension, Distribution, 42, (ulong)QuerySeed, 4);
        var fixtureKey = CreateFixtureIdentity(_dataset);
        FixturePreparation fixture = default;
        if (!s_built || !s_lastKey.Equals(fixtureKey.Key, StringComparison.Ordinal))
        {
            lock (s_gate)
            {
                if (!s_built || !s_lastKey.Equals(fixtureKey.Key, StringComparison.Ordinal))
                {
                    DisposeStaticResources();
                    fixture = EnsureSearchFixture(_dataset, fixtureKey);
                    s_hnswPath = fixture.HnswPath;
                    s_flatPath = fixture.FlatPath;
                    s_hnswMetrics = new DefaultMetricsCollector();
                    s_hnswSearcher = new LeanIndexSearcher(new LeanMMapDirectory(s_hnswPath), new IndexSearcherConfig
                    {
                        ParallelSearch = false,
                        EnableQueryCache = false,
                        Metrics = s_hnswMetrics,
                    });
                    s_flatSearcher = new LeanIndexSearcher(new LeanMMapDirectory(s_flatPath), new IndexSearcherConfig
                    {
                        ParallelSearch = false,
                        EnableQueryCache = false,
                    });
                    s_referenceRecords = _dataset.Records;
                    s_lastKey = fixtureKey.Key;
                    s_built = true;
                }
            }
        }
        else
        {
            fixture = new FixturePreparation(s_hnswPath, s_flatPath, fixtureKey.Key,
                FixtureReused: true, BuildEvidencePath: null);
        }

        if (string.IsNullOrWhiteSpace(fixture.Key))
            fixture = new FixturePreparation(s_hnswPath, s_flatPath, fixtureKey.Key,
                FixtureReused: true, BuildEvidencePath: null);
        _queryCase = _dataset.Query;
        _query = _queryCase.Vector;
        MetricsSnapshot metricsBefore = s_hnswMetrics.GetSnapshot();
        HnswSearchBudgetMeasurement[] searchBudgets = CreateQualificationBudgets(DocCount);

        var reference = s_flatSearcher.Search(new LeanVectorQuery("emb", _query, topK: 10), 10);
        _flatTopDocumentIds = reference.ScoreDocs
            .Select(static scoreDoc => scoreDoc.DocId)
            .ToArray();
        if (_flatTopDocumentIds.Length != 10)
            throw new InvalidOperationException($"Expected ten exact vector results, got {_flatTopDocumentIds.Length}.");

        var scalarTopDocumentIds = ComputeScalarTopDocumentIds();
        if (!scalarTopDocumentIds.SequenceEqual(_flatTopDocumentIds))
        {
            throw new InvalidOperationException(
                "LeanCorpus flat vector search does not agree with the scalar exact reference.");
        }
        _scalarChecksum = ResultChecksum(scalarTopDocumentIds);

        var exactOrdinalsByQueryAndK = new Dictionary<(string QueryId, int TopK), long[]>();
        var exactQueryResults = new List<VectorQueryQualityResult>(_dataset.Queries.Length);
        var qualityStopwatch = Stopwatch.StartNew();
        foreach (VectorQueryCase queryCase in _dataset.Queries)
        {
            var exactMeasurements = BenchmarkVectorQuality.Measure(
                _dataset,
                queryCase,
                topK =>
                {
                    var exact = s_flatSearcher.Search(new LeanVectorQuery("emb", queryCase.Vector, topK: topK), topK);
                    exactOrdinalsByQueryAndK[(queryCase.Id, topK)] = exact.ScoreDocs
                        .Select(hit => _dataset.Records[hit.DocId].Ordinal)
                        .ToArray();
                    return exact.ScoreDocs;
                },
                quantised: Quantisation == VectorQuantisation.Int8);
            exactQueryResults.Add(new VectorQueryQualityResult(queryCase.Id, exactMeasurements));
        }
        BenchmarkVectorData.WriteVectorQualityGroup(
            _dataset,
            "hnsw-exact",
            Quantisation.ToString().ToLowerInvariant(),
            new
            {
                quantisation = Quantisation.ToString(),
                topK = new[] { 1, 10, 100 },
                heldOutQueryIds = _dataset.Queries.Select(static query => query.Id).ToArray(),
            },
            exactQueryResults);
        BenchmarkVectorQuality.RequireHeldOutGroup(
            exactQueryResults,
            $"Exact vector search quantisation={Quantisation}, docCount={DocCount}, dimension={Dimension}");

        var graphOrdinalsByQueryAndK = new Dictionary<(string QueryId, int TopK), long[]>();
        var graphQueryResults = new List<VectorQueryQualityResult>(_dataset.Queries.Length);
        foreach (VectorQueryCase queryCase in _dataset.Queries)
        {
            var graphMeasurements = BenchmarkVectorQuality.Measure(
                _dataset,
                queryCase,
                topK =>
                {
                    var graph = s_hnswSearcher.Search(
                        CreateQualificationQuery("emb", queryCase.Vector, topK), topK);
                    graphOrdinalsByQueryAndK[(queryCase.Id, topK)] = graph.ScoreDocs
                        .Select(hit => _dataset.Records[hit.DocId].Ordinal)
                        .ToArray();
                    return graph.ScoreDocs;
                },
                quantised: Quantisation == VectorQuantisation.Int8);
            graphQueryResults.Add(new VectorQueryQualityResult(queryCase.Id, graphMeasurements));
        }
        MetricsSnapshot searchMetrics = GetSearchMetricsDelta(metricsBefore, s_hnswMetrics.GetSnapshot());

        var graphQuality = new HnswGraphQualityMeasurement[exactOrdinalsByQueryAndK.Count];
        var graphQualityIndex = 0;
        foreach (KeyValuePair<(string QueryId, int TopK), long[]> pair in exactOrdinalsByQueryAndK
            .OrderBy(static pair => pair.Key.QueryId, StringComparer.Ordinal)
            .ThenBy(static pair => pair.Key.TopK))
        {
            string queryId = pair.Key.QueryId;
            int topK = pair.Key.TopK;
            long[] exactOrdinals = pair.Value;
            long[] graphOrdinals = graphOrdinalsByQueryAndK[(queryId, topK)];
            var exactSet = exactOrdinals.ToHashSet();
            int hits = graphOrdinals.Distinct().Count(exactSet.Contains);
            graphQuality[graphQualityIndex++] = new HnswGraphQualityMeasurement(
                queryId,
                topK,
                exactOrdinals.Length,
                graphOrdinals.Length,
                (double)hits / topK);
        }

        qualityStopwatch.Stop();
        BenchmarkVectorData.WriteVectorQualityGroup(
            _dataset,
            "hnsw",
            $"{Quantisation.ToString().ToLowerInvariant()}-default",
            new
            {
                quantisation = Quantisation.ToString(),
                requestedEfSearch = "omitted",
                effectiveEfSearchByTopK = searchBudgets,
                graphBuild = new { m = GraphM, m0 = GraphM0, efConstruction = GraphEfConstruction, seed = GraphSeed },
                heldOutQueryIds = _dataset.Queries.Select(static query => query.Id).ToArray(),
            },
            graphQueryResults);

        setupStopwatch.Stop();
        WriteCaseSetupEvidence(_dataset, fixture, setupStopwatch.Elapsed, qualityStopwatch.Elapsed,
            graphQueryResults, Quantisation, graphQuality, searchMetrics, searchBudgets);

        BenchmarkVectorQuality.RequireHeldOutGroup(
            graphQueryResults,
            $"HNSW docCount={DocCount}, dimension={Dimension}, quantisation={Quantisation}, default efSearch");
        if (searchMetrics.HnswExhaustiveFallbackCount != 0 || searchMetrics.HnswVectorsScanned != 0)
        {
            throw new InvalidOperationException(
                $"Primary HNSW qualification used fallback scans: count={searchMetrics.HnswExhaustiveFallbackCount}, "
                + $"vectors={searchMetrics.HnswVectorsScanned}.");
        }

        var hnsw = s_hnswSearcher.Search(
            CreateQualificationQuery("emb", _query, topK: 10), 10);
        if (hnsw.ScoreDocs.Length != 10)
            throw new InvalidOperationException($"Expected ten HNSW results, got {hnsw.ScoreDocs.Length}.");
        _hnswChecksum = ResultChecksum(hnsw);
    }

    private static FixturePreparation EnsureSearchFixture(
        BenchmarkVectorDataset dataset,
        FixtureIdentity identity)
    {
        var fixtureRoot = Path.Combine(BenchmarkHelpers.TempRoot, "ll-hnsw-search-" + identity.Key);
        var hnswPath = Path.Combine(fixtureRoot, "hnsw");
        var flatPath = Path.Combine(fixtureRoot, "flat");
        var markerPath = Path.Combine(fixtureRoot, "fixture.identity");
        var lockPath = fixtureRoot + ".lock";

        using var fixtureLock = AcquireFixtureLock(lockPath);
        if (IsFixtureReady(markerPath, identity.Value, hnswPath, flatPath))
        {
            return new FixturePreparation(hnswPath, flatPath, identity.Key,
                FixtureReused: true,
                BuildEvidencePath: RelativeToArtifactDirectory(identity.BuildEvidencePath));
        }

        BenchmarkHelpers.DeleteDirectory(fixtureRoot);
        IODirectory.CreateDirectory(hnswPath);
        IODirectory.CreateDirectory(flatPath);

        var vectorMaterialisationStopwatch = Stopwatch.StartNew();
        var vectors = dataset.Records.Select(static record => record.Vector).ToArray();
        vectorMaterialisationStopwatch.Stop();

        var hnswBuild = BuildIndex(hnswPath, vectors, hnsw: true, identity.Quantisation);
        var flatBuild = BuildIndex(flatPath, vectors, hnsw: false, identity.Quantisation);
        var evidencePath = WriteFixtureBuildEvidence(dataset, identity, vectorMaterialisationStopwatch.Elapsed,
            hnswBuild, flatBuild, DirectorySize(hnswPath), DirectorySize(flatPath));
        var markerTemporaryPath = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var marker = new FileStream(markerTemporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(marker, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(identity.Value);
            }
            File.Move(markerTemporaryPath, markerPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(markerTemporaryPath))
                File.Delete(markerTemporaryPath);
        }

        return new FixturePreparation(hnswPath, flatPath, identity.Key,
            FixtureReused: false, BuildEvidencePath: RelativeToArtifactDirectory(evidencePath));
    }

    private FixtureIdentity CreateFixtureIdentity(BenchmarkVectorDataset dataset)
    {
        var assemblyId = typeof(HnswSearchBenchmarks).Assembly.ManifestModule.ModuleVersionId;
        var identity = string.Join('|',
            "hnsw-search-fixture-v1",
            dataset.Identity.ContentSha256,
            DocCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Dimension.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Quantisation.ToString(),
            GraphM.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GraphM0.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GraphEfConstruction.ToString(System.Globalization.CultureInfo.InvariantCulture),
            GraphSeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            assemblyId.ToString("N"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var key = Convert.ToHexString(hash).ToLowerInvariant()[..20];
        var artifactRoot = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        var evidencePath = string.IsNullOrWhiteSpace(artifactRoot)
            ? string.Empty
            : Path.Combine(Path.GetFullPath(artifactRoot), "hnsw-setup", "fixture-" + key + ".json");
        return new FixtureIdentity(key, identity, evidencePath, Quantisation);
    }

    private static bool IsFixtureReady(string markerPath, string identity, string hnswPath, string flatPath)
    {
        return File.Exists(markerPath)
            && Directory.Exists(hnswPath)
            && Directory.Exists(flatPath)
            && File.ReadAllText(markerPath).TrimStart('\uFEFF').Equals(identity, StringComparison.Ordinal);
    }

    private static FileStream AcquireFixtureLock(string path)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromMinutes(30))
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(100));
            }
        }

        throw new TimeoutException($"Timed out waiting for HNSW benchmark fixture lock '{path}'.");
    }

    private static IndexBuildEvidence BuildIndex(string path, float[][] vectors, bool hnsw, VectorQuantisation quantisation)
    {
        var metrics = new DefaultMetricsCollector();
        var config = new LeanIndexWriterConfig
        {
            // Keep the frozen primary fixture in dataset-record order by buffering
            // the complete input in one DWPT and flushing it once at Commit.
            IndexingConcurrency = 1,
            RamBufferSizeMB = 0,
            RamPerThreadHardLimitMB = 0,
            MaxBufferedDocs = checked(vectors.Length + 1),
            BuildHnswOnFlush = hnsw,
            NormaliseVectors = true,
            VectorQuantisation = quantisation,
            HnswBuildConfig = CreateGraphBuildConfig(),
            HnswSeed = GraphSeed,
            Metrics = metrics,
        };

        var totalStopwatch = Stopwatch.StartNew();
        var addDocumentsStopwatch = Stopwatch.StartNew();
        TimeSpan addDocumentsElapsed;
        TimeSpan commitElapsed;
        using var writer = new LeanIndexWriter(new LeanMMapDirectory(path), config);
        for (int i = 0; i < vectors.Length; i++)
        {
            var document = new LeanDocument();
            document.Add(new VectorField("emb", new ReadOnlyMemory<float>(vectors[i])));
            writer.AddDocument(document);
        }
        addDocumentsStopwatch.Stop();
        addDocumentsElapsed = addDocumentsStopwatch.Elapsed;
        var commitStopwatch = Stopwatch.StartNew();
        writer.Commit();
        commitStopwatch.Stop();
        commitElapsed = commitStopwatch.Elapsed;
        IReadOnlyList<SegmentInfo> segments = writer.GetNrtSegments();
        if (segments.Count != 1 || segments[0].DocCount != vectors.Length || segments[0].LiveDocCount != vectors.Length)
        {
            throw new InvalidOperationException(
                $"Vector benchmark fixture expected one live {vectors.Length}-document segment in source order, " +
                $"but found {segments.Count} segment(s) with {string.Join(", ", segments.Select(static segment => $"{segment.DocCount}/{segment.LiveDocCount}"))} documents.");
        }

        totalStopwatch.Stop();

        var snapshot = metrics.GetSnapshot();
        return new IndexBuildEvidence(totalStopwatch.Elapsed, addDocumentsElapsed, commitElapsed,
            segments.Count, segments[0].DocCount, segments[0], snapshot.HnswBuildCount, snapshot.HnswBuildTotalMs);
    }

    private static string WriteFixtureBuildEvidence(
        BenchmarkVectorDataset dataset,
        FixtureIdentity identity,
        TimeSpan vectorMaterialisationElapsed,
        IndexBuildEvidence hnswBuild,
        IndexBuildEvidence flatBuild,
        long hnswIndexBytes,
        long flatIndexBytes)
    {
        var path = identity.BuildEvidencePath;
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        WriteJsonAtomically(path, new
        {
            schemaVersion = 1,
            fixtureKey = identity.Key,
            fixtureIdentity = identity.Value,
            dataset = new
            {
                profileId = dataset.Identity.ProfileId,
                profileVersion = dataset.Identity.ProfileVersion,
                seed = dataset.Identity.Seed,
                recordCount = dataset.Identity.RecordCount,
                contentSha256 = dataset.Identity.ContentSha256,
                dimension = dataset.Query.Vector.Length,
            },
            quantisation = identity.Quantisation.ToString(),
            graph = new { m = GraphM, m0 = GraphM0, efConstruction = GraphEfConstruction, seed = GraphSeed },
            vectorMaterialisationMilliseconds = vectorMaterialisationElapsed.TotalMilliseconds,
            hnswIndexBuild = new
            {
                totalMilliseconds = hnswBuild.TotalElapsed.TotalMilliseconds,
                documentAdditionMilliseconds = hnswBuild.AddDocumentsElapsed.TotalMilliseconds,
                commitMilliseconds = hnswBuild.CommitElapsed.TotalMilliseconds,
                segmentCount = hnswBuild.SegmentCount,
                segmentDocumentCount = hnswBuild.SegmentDocumentCount,
                documentOrder = "source ordinal",
                graphBuildCount = hnswBuild.HnswBuildCount,
                graphBuildMilliseconds = hnswBuild.HnswBuildMilliseconds,
                indexBytes = hnswIndexBytes,
            },
            flatIndexBuild = new
            {
                totalMilliseconds = flatBuild.TotalElapsed.TotalMilliseconds,
                documentAdditionMilliseconds = flatBuild.AddDocumentsElapsed.TotalMilliseconds,
                commitMilliseconds = flatBuild.CommitElapsed.TotalMilliseconds,
                segmentCount = flatBuild.SegmentCount,
                segmentDocumentCount = flatBuild.SegmentDocumentCount,
                documentOrder = "source ordinal",
                indexBytes = flatIndexBytes,
            },
            fixtureBuildUtc = DateTimeOffset.UtcNow,
        });
        return path;
    }

    private void WriteCaseSetupEvidence(
        BenchmarkVectorDataset dataset,
        FixturePreparation fixture,
        TimeSpan setupElapsed,
        TimeSpan qualityElapsed,
        IReadOnlyList<VectorQueryQualityResult> qualityMeasurements,
        VectorQuantisation quantisation,
        IReadOnlyList<HnswGraphQualityMeasurement> graphQualityMeasurements,
        MetricsSnapshot searchMetrics,
        IReadOnlyList<HnswSearchBudgetMeasurement> searchBudgets)
    {
        var artifactRoot = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        if (string.IsNullOrWhiteSpace(artifactRoot))
            return;

        var path = Path.Combine(Path.GetFullPath(artifactRoot), "hnsw-setup", "cases",
            $"process-{Environment.ProcessId}-doc{DocCount}-dim{Dimension}-{Distribution.ToString().ToLowerInvariant()}-q{QuerySeed}-{quantisation.ToString().ToLowerInvariant()}-default.json");
        WriteJsonAtomically(path, new
        {
            schemaVersion = 1,
            processId = Environment.ProcessId,
            fixtureKey = fixture.Key,
            fixtureReused = fixture.FixtureReused,
            fixtureBuildEvidencePath = fixture.BuildEvidencePath,
            documentCount = DocCount,
            dimension = Dimension,
            distribution = Distribution.ToString(),
            querySeed = QuerySeed,
            quantisation = quantisation.ToString(),
            requestedEfSearch = "omitted",
            effectiveEfSearchByTopK = searchBudgets,
            graph = new { m = GraphM, m0 = GraphM0, efConstruction = GraphEfConstruction, seed = GraphSeed },
            dataset = new
            {
                profileId = dataset.Identity.ProfileId,
                profileVersion = dataset.Identity.ProfileVersion,
                recordCount = dataset.Identity.RecordCount,
                contentSha256 = dataset.Identity.ContentSha256,
                querySeed = dataset.QuerySeed,
                queryIds = dataset.Queries.Select(static query => query.Id).ToArray(),
            },
            setupMilliseconds = setupElapsed.TotalMilliseconds,
            qualityPreflightMilliseconds = qualityElapsed.TotalMilliseconds,
            qualityMeasurements,
            graphQualityMeasurements,
            searchMetrics = new
            {
                searchCount = searchMetrics.HnswSearchCount,
                graphNodeVisits = searchMetrics.HnswNodesVisited,
                vectorsScanned = searchMetrics.HnswVectorsScanned,
                exhaustiveFallbackCount = searchMetrics.HnswExhaustiveFallbackCount,
                selectiveFilterScanCount = searchMetrics.HnswSelectiveFilterScanCount,
            },
        });
    }

    private static MetricsSnapshot GetSearchMetricsDelta(MetricsSnapshot before, MetricsSnapshot after)
        => new()
        {
            HnswSearchCount = after.HnswSearchCount - before.HnswSearchCount,
            HnswNodesVisited = after.HnswNodesVisited - before.HnswNodesVisited,
            HnswVectorsScanned = after.HnswVectorsScanned - before.HnswVectorsScanned,
            HnswExhaustiveFallbackCount = after.HnswExhaustiveFallbackCount - before.HnswExhaustiveFallbackCount,
            HnswSelectiveFilterScanCount = after.HnswSelectiveFilterScanCount - before.HnswSelectiveFilterScanCount,
        };

    private static void WriteJsonAtomically(string path, object value)
    {
        IODirectory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static long DirectorySize(string path)
        => IODirectory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(static filePath => new FileInfo(filePath).Length);

    private static string? RelativeToArtifactDirectory(string path)
    {
        var artifactRoot = Environment.GetEnvironmentVariable("LEANCORPUS_ARTIFACT_DIR");
        return string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(artifactRoot)
            ? null
            : Path.GetRelativePath(Path.GetFullPath(artifactRoot), path).Replace('\\', '/');
    }

    [Benchmark(Baseline = true, Description = "Exact flat scan")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FlatScan()
    {
        var result = s_flatSearcher.Search(new LeanVectorQuery("emb", _query, topK: 10), 10);
        ValidateExactResult(result);
        return ResultChecksum(result);
    }

    [Benchmark(Description = "HNSW two-phase")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Hnsw()
    {
        var result = s_hnswSearcher.Search(
            CreateQualificationQuery("emb", _query, topK: 10), 10);
        if (result.ScoreDocs.Length != 10)
            throw new InvalidOperationException($"Expected ten HNSW results, got {result.ScoreDocs.Length}.");
        int checksum = ResultChecksum(result);
        if (checksum != _hnswChecksum)
            throw new InvalidOperationException("The HNSW result changed from the setup fixture.");
        return checksum;
    }

    [Benchmark(Description = "Reference scalar flat scan")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Reference_ScalarFlatScan()
    {
        var documentIds = ComputeScalarTopDocumentIds();
        int checksum = ResultChecksum(documentIds);
        if (checksum != _scalarChecksum)
            throw new InvalidOperationException("The scalar exact result changed from the setup fixture.");
        return checksum;
    }

    private int[] ComputeScalarTopDocumentIds()
    {
        const int topK = 10;
        return VectorGroundTruth.Compute(s_referenceRecords, _queryCase, topK)
            .NeighbourOrdinals.Select(static ordinal => checked((int)ordinal)).ToArray();
    }

    /// <summary>Release shared resources after all HNSW parameter rows complete.</summary>
    public static void CleanupLuceneResources() => DisposeStaticResources();

    [GlobalCleanup]
    public void Cleanup() => DisposeStaticResources();

    private void ValidateExactResult(TopDocs result)
    {
        var ids = result.ScoreDocs.Select(static scoreDoc => scoreDoc.DocId);
        if (!ids.SequenceEqual(_flatTopDocumentIds))
            throw new InvalidOperationException("The flat vector result changed from the setup reference.");
    }

    private static int ResultChecksum(TopDocs result)
        => ResultChecksum(result.ScoreDocs.Select(static scoreDoc => scoreDoc.DocId));

    private static int ResultChecksum(IEnumerable<int> documentIds)
    {
        int checksum = 17;
        foreach (int documentId in documentIds)
            checksum = unchecked((checksum * 31) + documentId);
        return checksum;
    }

    private static void DisposeStaticResources()
    {
        if (s_built)
        {
            s_hnswSearcher.Dispose();
            s_flatSearcher.Dispose();
        }

        s_built = false;
        s_lastKey = string.Empty;
        s_hnswPath = string.Empty;
        s_flatPath = string.Empty;
        s_referenceRecords = [];
    }

    private readonly record struct FixtureIdentity(
        string Key,
        string Value,
        string BuildEvidencePath,
        VectorQuantisation Quantisation);

    private readonly record struct FixturePreparation(
        string HnswPath,
        string FlatPath,
        string Key,
        bool FixtureReused,
        string? BuildEvidencePath);

    private readonly record struct IndexBuildEvidence(
        TimeSpan TotalElapsed,
        TimeSpan AddDocumentsElapsed,
        TimeSpan CommitElapsed,
        int SegmentCount,
        int SegmentDocumentCount,
        SegmentInfo Segment,
        long HnswBuildCount,
        long HnswBuildMilliseconds);
}
