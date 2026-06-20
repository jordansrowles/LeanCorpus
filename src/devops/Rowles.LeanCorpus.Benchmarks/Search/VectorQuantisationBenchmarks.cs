using BenchmarkDotNet.Attributes;
using System.Runtime.CompilerServices;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;

// Lucene.NET aliases
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
/// Measures HNSW search throughput across
/// <see cref="VectorQuantisation"/> levels: None (float32 baseline),
/// Int8 (scalar quantisation), and BBQ (binary quantisation).
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class VectorQuantisationBenchmarks
{
    [Params(1_000, 10_000)]
    public int DocCount { get; set; }

    [Params(64, 128)]
    public int Dimension { get; set; }

    /// <summary>Quantisation strategy applied at index time.</summary>
    [Params(VectorQuantisation.None, VectorQuantisation.Int8, VectorQuantisation.BBQ)]
    public VectorQuantisation Quantisation { get; set; }

    private const string FieldName = "emb";
    private const int TopK = 10;

    // Index state — guarded by (DocCount, Dimension, Quantisation) key
    private static readonly System.Threading.Lock s_gate = new();
    private static (int docCount, int dim, VectorQuantisation q) s_lastKey;
    private static bool s_built;
    private static string s_indexPath = string.Empty;
    private static LeanIndexSearcher s_searcher = default!;

    // Lucene.NET index state — guarded separately by (DocCount, Dimension)
    private static readonly System.Threading.Lock s_luceneGate = new();
    private static (int docCount, int dim) s_luceneLastKey;
    private static bool s_luceneBuilt;
    private static LuceneRAMDirectory? s_luceneDirectory;
    private static LuceneDirectoryReader? s_luceneReader;
    private static float[][] s_luceneVectors = [];

    private float[] _query = [];

    [GlobalSetup]
    public void Setup()
    {
        var key = (DocCount, Dimension, Quantisation);
        if (!s_built || s_lastKey != key)
        {
            lock (s_gate)
            {
                if (!s_built || s_lastKey != key)
                {
                    s_indexPath = Path.Combine(BenchmarkHelpers.TempRoot,
                        "lc_vq_bench_" + Guid.NewGuid().ToString("N"));
                    IODirectory.CreateDirectory(s_indexPath);

                    var rnd = new Random(7);
                    var vectors = new float[DocCount][];
                    for (int i = 0; i < DocCount; i++)
                    {
                        var v = new float[Dimension];
                        for (int d = 0; d < Dimension; d++)
                            v[d] = (float)(rnd.NextDouble() * 2 - 1);
                        vectors[i] = v;
                    }

                    var cfg = new LeanIndexWriterConfig
                    {
                        BuildHnswOnFlush = true,
                        NormaliseVectors = true,
                        VectorQuantisation = Quantisation,
                        HnswBuildConfig = new HnswBuildConfig
                            { M = 16, M0 = 32, EfConstruction = 100 },
                        HnswSeed = 1L,
                    };

                    using var writer = new LeanIndexWriter(
                        new LeanMMapDirectory(s_indexPath), cfg);
                    for (int i = 0; i < vectors.Length; i++)
                    {
                        var doc = new LeanDocument();
                        doc.Add(new VectorField(FieldName,
                            new ReadOnlyMemory<float>(vectors[i])));
                        writer.AddDocument(doc);
                    }
                    writer.Commit();

                    s_searcher = new LeanIndexSearcher(
                        new LeanMMapDirectory(s_indexPath));

                    EnsureLuceneIndex(vectors);

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

    [Benchmark(Baseline = true, Description = "HNSW search")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Search()
    {
        var q = new VectorQuery(FieldName, _query, topK: TopK);
        return s_searcher.Search(q, TopK).TotalHits;
    }

    [Benchmark(Description = "Lucene.NET flat scan")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Baseline()
    {
        int topK = TopK;
        var heap = new (float Similarity, int DocId)[topK];
        int heapSize = 0;
        int dimension = Dimension;

        for (int i = 0; i < s_luceneReader!.NumDocs; i++)
        {
            var doc = s_luceneReader.Document(i);
            var stored = doc.GetBinaryValue(FieldName);
            if (stored is null)
                continue;
            var vec = new float[stored.Length / sizeof(float)];
            Buffer.BlockCopy(stored.Bytes, stored.Offset, vec, 0, stored.Length);

            float dot = 0f;
            for (int d = 0; d < dimension; d++)
                dot += vec[d] * _query[d];

            if (heapSize < topK || dot > heap[0].Similarity)
            {
                if (heapSize < topK)
                {
                    heap[heapSize++] = (dot, i);
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

    private static void EnsureLuceneIndex(float[][] vectors)
    {
        var key = (DocCount: vectors.Length, Dim: vectors[0].Length);
        if (s_luceneBuilt && s_luceneLastKey == key)
            return;

        lock (s_luceneGate)
        {
            if (s_luceneBuilt && s_luceneLastKey == key)
                return;

            s_luceneDirectory = new LuceneRAMDirectory();
            var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            using var writer = new LuceneIndexWriter(
                s_luceneDirectory,
                new LuceneIndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
            for (int i = 0; i < vectors.Length; i++)
            {
                var doc = new LuceneDocument();
                var bytes = new byte[vectors[i].Length * sizeof(float)];
                Buffer.BlockCopy(vectors[i], 0, bytes, 0, bytes.Length);
                doc.Add(new LuceneStoredField(FieldName, new LuceneBytesRef(bytes)));
                writer.AddDocument(doc);
            }
            writer.Commit();
            s_luceneReader = LuceneDirectoryReader.Open(s_luceneDirectory);
            s_luceneVectors = vectors;
            s_luceneLastKey = key;
            s_luceneBuilt = true;
        }
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
