using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Fst;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using LeanIndexWriter = Rowles.LeanLucene.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanLucene.Index.Indexer.IndexWriterConfig;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanVectorQuery = Rowles.LeanLucene.Search.Queries.VectorQuery;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures HNSW two-phase search latency vs the legacy flat O(n) cosine scan
/// across realistic dataset sizes and dimensions.
/// </summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class HnswSearchBenchmarks
{
    [Params(1_000, 10_000)]
    public int DocCount { get; set; }

    [Params(64, 128)]
    public int Dimension { get; set; }

    private string _hnswPath = string.Empty;
    private string _flatPath = string.Empty;
    private LeanIndexSearcher _hnswSearcher = default!;
    private LeanIndexSearcher _flatSearcher = default!;
    private float[] _query = [];

    [GlobalSetup]
    public void Setup()
    {
        _hnswPath = Path.Combine(Path.GetTempPath(), "ll_hnsw_bench_" + Guid.NewGuid().ToString("N"));
        _flatPath = Path.Combine(Path.GetTempPath(), "ll_flat_bench_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_hnswPath);
        Directory.CreateDirectory(_flatPath);

        var rnd = new Random(7);
        var vectors = new float[DocCount][];
        for (int i = 0; i < DocCount; i++)
        {
            var v = new float[Dimension];
            for (int d = 0; d < Dimension; d++)
                v[d] = (float)(rnd.NextDouble() * 2 - 1);
            vectors[i] = v;
        }

        BuildIndex(_hnswPath, vectors, hnsw: true);
        BuildIndex(_flatPath, vectors, hnsw: false);

        _hnswSearcher = new LeanIndexSearcher(new MMapDirectory(_hnswPath));
        _flatSearcher = new LeanIndexSearcher(new MMapDirectory(_flatPath));

        _query = new float[Dimension];
        for (int d = 0; d < Dimension; d++) _query[d] = (float)(rnd.NextDouble() * 2 - 1);
    }

    private static void BuildIndex(string path, float[][] vectors, bool hnsw)
    {
        var cfg = new LeanIndexWriterConfig
        {
            BuildHnswOnFlush = hnsw,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1L,
        };
        using var writer = new LeanIndexWriter(new MMapDirectory(path), cfg);
        for (int i = 0; i < vectors.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(vectors[i])));
            writer.AddDocument(doc);
        }
        writer.Commit();
    }

    [Benchmark(Baseline = true, Description = "Flat scan")]
    public int FlatScan()
    {
        var q = new LeanVectorQuery("emb", _query, topK: 10);
        return _flatSearcher.Search(q, 10).TotalHits;
    }

    [Benchmark(Description = "HNSW two-phase")]
    public int Hnsw()
    {
        var q = new LeanVectorQuery("emb", _query, topK: 10, efSearch: 64);
        return _hnswSearcher.Search(q, 10).TotalHits;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _hnswSearcher.Dispose();
        _flatSearcher.Dispose();
        try { Directory.Delete(_hnswPath, true); } catch { }
        try { Directory.Delete(_flatPath, true); } catch { }
    }
}
