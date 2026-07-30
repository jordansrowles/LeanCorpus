using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures the bounded hybrid paths introduced by Hybrid Retrieval 2.0: filter-aware
/// HNSW planning and learned-sparse fusion seeds.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class HybridRetrievalBenchmarks
{
    [Params(64, 128)]
    public int Dimension { get; set; }

    /// <summary>Percentage of documents admitted by the vector filter.</summary>
    [Params(1, 10)]
    public int FilterPercentage { get; set; }

    /// <summary>Document count, overridden by the runner's <c>--doccount</c> option when supplied.</summary>
    public int DocCount { get; set; } = 10_000;

    private const string VectorFieldName = "emb";
    private const string FilterFieldName = "tier";
    private const string SparseFieldName = "impact";
    private const int TopK = 10;

    private IndexSearcher _graphSearcher = default!;
    private IndexSearcher _flatSearcher = default!;
    private VectorQuery _filteredQuery = default!;
    private VectorQuery _fullVectorQuery = default!;
    private FusionQuery _fusionWithoutSeeds = default!;
    private FusionQuery _fusionWithSeeds = default!;

    [GlobalSetup]
    public void Setup()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("BENCH_DOC_COUNT"), out int configuredDocCount) &&
            configuredDocCount > 0)
        {
            DocCount = configuredDocCount;
        }

        string root = Path.Combine(
            BenchmarkHelpers.TempRoot,
            $"lc_hybrid_{DocCount}_{Dimension}_{FilterPercentage}_{Guid.NewGuid():N}");
        string graphPath = Path.Combine(root, "graph");
        string flatPath = Path.Combine(root, "flat");
        Directory.CreateDirectory(graphPath);
        Directory.CreateDirectory(flatPath);

        var random = new Random(37);
        var vectors = new float[DocCount][];
        for (int document = 0; document < vectors.Length; document++)
        {
            var vector = new float[Dimension];
            for (int dimension = 0; dimension < vector.Length; dimension++)
                vector[dimension] = (float)(random.NextDouble() * 2d - 1d);
            vectors[document] = vector;
        }

        BuildIndex(graphPath, vectors, buildHnsw: true);
        BuildIndex(flatPath, vectors, buildHnsw: false);
        _graphSearcher = new IndexSearcher(new MMapDirectory(graphPath));
        _flatSearcher = new IndexSearcher(new MMapDirectory(flatPath));

        var queryVector = new float[Dimension];
        var queryRandom = new Random(53);
        for (int dimension = 0; dimension < queryVector.Length; dimension++)
            queryVector[dimension] = (float)(queryRandom.NextDouble() * 2d - 1d);

        var filter = new TermQuery(FilterFieldName, "selected");
        _filteredQuery = new VectorQuery(
            VectorFieldName,
            queryVector,
            topK: TopK,
            efSearch: 64,
            filter: filter);
        _fullVectorQuery = new VectorQuery(
            VectorFieldName,
            queryVector,
            topK: TopK,
            efSearch: 64);
        _fusionWithoutSeeds = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new VectorQuery(VectorFieldName, queryVector, topK: TopK, efSearch: 64), candidateWindow: 32)
            .Add(new SparseImpactQuery(SparseFieldName, [new SparseImpact("alpha", 1f)]), candidateWindow: 32);
        _fusionWithSeeds = new FusionQuery(FusionMethod.WeightedRrf)
            .Add(new VectorQuery(VectorFieldName, queryVector, topK: TopK, efSearch: 64), candidateWindow: 32)
            .Add(new SparseImpactQuery(SparseFieldName, [new SparseImpact("alpha", 1f)]), candidateWindow: 32)
            .UseSparseVectorSeeds(candidateLimit: 32);
        WritePlannerArtefact();
    }

    [Benchmark(Baseline = true, Description = "Filtered exact scan")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FilteredExactScan() => _flatSearcher.Search(_filteredQuery, TopK).TotalHits;

    [Benchmark(Description = "Filtered HNSW planner")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FilteredHnswPlanner() => _graphSearcher.Search(_filteredQuery, TopK).TotalHits;

    [Benchmark(Description = "Full-vector HNSW")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FullVectorHnsw() => _graphSearcher.Search(_fullVectorQuery, TopK).TotalHits;

    [Benchmark(Description = "Sparse+dense fusion")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FusionWithoutSparseSeeds() => _graphSearcher.Search(_fusionWithoutSeeds, TopK).TotalHits;

    [Benchmark(Description = "Sparse-seeded dense fusion")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FusionWithSparseSeeds() => _graphSearcher.Search(_fusionWithSeeds, TopK).TotalHits;

    [GlobalCleanup]
    public void Cleanup()
    {
        _graphSearcher.Dispose();
        _flatSearcher.Dispose();
    }

    private void BuildIndex(string path, float[][] vectors, bool buildHnsw)
    {
        using var writer = new IndexWriter(
            new MMapDirectory(path),
            new IndexWriterConfig
            {
                BuildHnswOnFlush = buildHnsw,
                HnswSeed = 1L,
                HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
                VectorFields =
                {
                    [VectorFieldName] = new VectorFieldConfig
                    {
                        BuildHnsw = buildHnsw,
                        Normalise = true,
                        HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
                    },
                },
            });
        for (int document = 0; document < vectors.Length; document++)
        {
            bool selected = document % 100 < FilterPercentage;
            var indexed = new LeanDocument();
            indexed.Add(new VectorField(VectorFieldName, vectors[document]));
            indexed.Add(new TextField(FilterFieldName, selected ? "selected" : "other"));
            indexed.Add(new SparseImpactField(
                SparseFieldName,
                [new SparseImpact(selected ? "alpha" : "beta", selected ? 2f : 1f)]));
            writer.AddDocument(indexed);
        }
        writer.Commit();
    }

    private void WritePlannerArtefact()
    {
        string? runDirectory = Environment.GetEnvironmentVariable("BENCH_RUN_DIRECTORY");
        if (string.IsNullOrWhiteSpace(runDirectory))
            return;

        var exact = _flatSearcher.Search(_filteredQuery, TopK).ScoreDocs;
        var planned = _graphSearcher.SearchWithDiagnostics(_filteredQuery, TopK);
        int overlap = planned.Results.ScoreDocs.Count(hit => exact.Any(reference => reference.DocId == hit.DocId));
        int explainedDocument = exact.Length > 0 ? exact[0].DocId : 0;
        string plannerStrategy = _graphSearcher.Explain(_filteredQuery, explainedDocument)?.Description
            ?? "no eligible vector document";
        var artefact = new HybridPlannerGateArtefact(
            DocCount,
            Dimension,
            FilterPercentage,
            plannerStrategy,
            exact.Length == 0 ? 1d : (double)overlap / exact.Length,
            _fusionWithSeeds.SparseSeedCandidateLimit,
            _fusionWithSeeds.Children.Sum(child => child.CandidateWindow));
        string fileName = $"hybrid-planner-gate-{DocCount}-{Dimension}-{FilterPercentage}.json";
        File.WriteAllText(
            Path.Combine(runDirectory, fileName),
            JsonSerializer.Serialize(artefact, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record HybridPlannerGateArtefact(
        int DocumentCount,
        int Dimension,
        int FilterPercentage,
        string Strategy,
        double RecallAt10,
        int SparseSeedCandidateLimit,
        int TotalFusionCandidateWindow);
}
