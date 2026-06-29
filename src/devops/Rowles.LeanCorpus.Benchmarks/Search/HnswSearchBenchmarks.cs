using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanVectorQuery = Rowles.LeanCorpus.Search.Queries.VectorQuery;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;

// Lucene.NET aliases (avoid ambiguity with LeanCorpus types)
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStoredField = Lucene.Net.Documents.StoredField;
using LuceneIndexWriter = Lucene.Net.Index.IndexWriter;
using LuceneIndexWriterConfig = Lucene.Net.Index.IndexWriterConfig;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneRAMDirectory = Lucene.Net.Store.RAMDirectory;
using LuceneBytesRef = Lucene.Net.Util.BytesRef;
using IODirectory = System.IO.Directory;

namespace Rowles.LeanCorpus.Benchmarks;

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

    // Index state — guarded by (DocCount, Dimension) key
    private static readonly System.Threading.Lock s_gate = new();
    private static (int docCount, int dim) s_lastKey;
    private static bool s_built;
    private static string s_hnswPath = string.Empty;
    private static string s_flatPath = string.Empty;
    private static LeanIndexSearcher s_hnswSearcher = default!;
    private static LeanIndexSearcher s_flatSearcher = default!;

    // Lucene.NET index state
    private static LuceneRAMDirectory? s_luceneDirectory;
    private static LuceneDirectoryReader? s_luceneReader;
    private static float[][] s_luceneVectors = [];
    private static string s_luceneIndexPath = string.Empty;

    private float[] _query = [];

    [GlobalSetup]
    public void Setup()
    {
        var key = (DocCount, Dimension);
        if (!s_built || s_lastKey != key)
        {
            lock (s_gate)
            {
                if (!s_built || s_lastKey != key)
                {
                    s_hnswPath = Path.Combine(BenchmarkHelpers.TempRoot,
                        "ll_hnsw_bench_" + Guid.NewGuid().ToString("N"));
                    s_flatPath = Path.Combine(BenchmarkHelpers.TempRoot,
                        "ll_flat_bench_" + Guid.NewGuid().ToString("N"));
                    IODirectory.CreateDirectory(s_hnswPath);
                    IODirectory.CreateDirectory(s_flatPath);

                    var rnd = new Random(7);
                    var vectors = new float[DocCount][];
                    for (int i = 0; i < DocCount; i++)
                    {
                        var v = new float[Dimension];
                        for (int d = 0; d < Dimension; d++)
                            v[d] = (float)(rnd.NextDouble() * 2 - 1);
                        vectors[i] = v;
                    }

                    BuildIndex(s_hnswPath, vectors, hnsw: true);
                    BuildIndex(s_flatPath, vectors, hnsw: false);

                    s_hnswSearcher = new LeanIndexSearcher(new LeanMMapDirectory(s_hnswPath));
                    s_flatSearcher = new LeanIndexSearcher(new LeanMMapDirectory(s_flatPath));

                    BuildLuceneIndex(vectors);

                    s_lastKey = key;
                    s_built = true;
                }
            }
        }

        _query = new float[Dimension];
        var qrnd = new Random(7);
        for (int d = 0; d < Dimension; d++)
            _query[d] = (float)(qrnd.NextDouble() * 2 - 1);
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
        using var writer = new LeanIndexWriter(new LeanMMapDirectory(path), cfg);
        for (int i = 0; i < vectors.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new VectorField("emb", new ReadOnlyMemory<float>(vectors[i])));
            writer.AddDocument(doc);
        }
        writer.Commit();
    }

    private static void BuildLuceneIndex(float[][] vectors)
    {
        s_luceneDirectory = new LuceneRAMDirectory();
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new LuceneIndexWriter(
            s_luceneDirectory,
            new LuceneIndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        for (int i = 0; i < vectors.Length; i++)
        {
            var doc = new LuceneDocument();
            // Store the vector as a binary stored field.
            var bytes = new byte[vectors[i].Length * sizeof(float)];
            Buffer.BlockCopy(vectors[i], 0, bytes, 0, bytes.Length);
            doc.Add(new LuceneStoredField("emb", new LuceneBytesRef(bytes)));
            writer.AddDocument(doc);
        }
        writer.Commit();
        s_luceneReader = LuceneDirectoryReader.Open(s_luceneDirectory);
        s_luceneVectors = vectors;
    }

    [Benchmark(Baseline = true, Description = "Flat scan")]
    public int FlatScan()
    {
        var q = new LeanVectorQuery("emb", _query, topK: 10);
        return s_flatSearcher.Search(q, 10).TotalHits;
    }

    [Benchmark(Description = "HNSW two-phase")]
    public int Hnsw()
    {
        var q = new LeanVectorQuery("emb", _query, topK: 10, efSearch: 64);
        return s_hnswSearcher.Search(q, 10).TotalHits;
    }

    [Benchmark(Description = "Lucene.NET flat scan")]
    public int LuceneNet_FlatScan()
    {
        // Brute-force cosine scan over all vectors stored in Lucene.NET.
        int topK = 10;
        var heap = new (float Similarity, int DocId)[topK];
        int heapSize = 0;
        int dimension = Dimension;

        for (int i = 0; i < s_luceneReader!.NumDocs; i++)
        {
            var doc = s_luceneReader.Document(i);
            var stored = doc.GetBinaryValue("emb");
            if (stored is null)
                continue;
            var vec = new float[stored.Length / sizeof(float)];
            Buffer.BlockCopy(stored.Bytes, stored.Offset, vec, 0, stored.Length);

            // Cosine similarity: dot product of normalised vectors.
            float dot = 0f;
            for (int d = 0; d < dimension; d++)
                dot += vec[d] * _query[d];

            if (heapSize < topK || dot > heap[0].Similarity)
            {
                if (heapSize < topK)
                {
                    heap[heapSize++] = (dot, i);
                    // Bubble up.
                    int c = heapSize - 1;
                    while (c > 0 && heap[c].Similarity < heap[(c - 1) / 2].Similarity)
                    {
                        (heap[c], heap[(c - 1) / 2]) = (heap[(c - 1) / 2], heap[c]);
                        c = (c - 1) / 2;
                    }
                }
                else
                {
                    heap[0] = (dot, i);
                    // Sift down.
                    int p = 0;
                    while (true)
                    {
                        int smallest = p;
                        int left = 2 * p + 1;
                        int right = 2 * p + 2;
                        if (left < heapSize && heap[left].Similarity < heap[smallest].Similarity)
                            smallest = left;
                        if (right < heapSize && heap[right].Similarity < heap[smallest].Similarity)
                            smallest = right;
                        if (smallest == p)
                            break;
                        (heap[p], heap[smallest]) = (heap[smallest], heap[p]);
                        p = smallest;
                    }
                }
            }
        }
        return heapSize;
    }

    /// <summary>Release static Lucene.NET resources.</summary>
    public static void CleanupLuceneResources()
    {
        s_luceneReader?.Dispose();
        s_luceneReader = null;
        s_luceneDirectory?.Dispose();
        s_luceneDirectory = null;
        s_luceneVectors = [];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Static resources persist for class lifetime.
    }
}
