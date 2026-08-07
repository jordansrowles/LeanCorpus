using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
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
    [Params(1_000, 10_000)]
    public int DocCount { get; set; }

    [Params(64, 128)]
    public int Dimension { get; set; }

    [Params(64, 256)]
    public int EfSearch { get; set; }

    private static readonly System.Threading.Lock s_gate = new();
    private static (int docCount, int dim) s_lastKey;
    private static bool s_built;
    private static string s_hnswPath = string.Empty;
    private static string s_flatPath = string.Empty;
    private static LeanIndexSearcher s_hnswSearcher = default!;
    private static LeanIndexSearcher s_flatSearcher = default!;
    private static float[][] s_referenceVectors = [];

    private float[] _query = [];
    private int[] _flatTopDocumentIds = [];
    private int _scalarChecksum;
    private int _hnswChecksum;

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
                    DisposeStaticResources();
                    s_hnswPath = Path.Combine(BenchmarkHelpers.TempRoot,
                        "ll-hnsw-bench-" + Guid.NewGuid().ToString("N"));
                    s_flatPath = Path.Combine(BenchmarkHelpers.TempRoot,
                        "ll-flat-bench-" + Guid.NewGuid().ToString("N"));
                    IODirectory.CreateDirectory(s_hnswPath);
                    IODirectory.CreateDirectory(s_flatPath);

                    var random = new Random(7);
                    var vectors = new float[DocCount][];
                    for (int i = 0; i < DocCount; i++)
                    {
                        var vector = new float[Dimension];
                        for (int dimension = 0; dimension < Dimension; dimension++)
                            vector[dimension] = (float)(random.NextDouble() * 2 - 1);
                        vectors[i] = vector;
                    }

                    BuildIndex(s_hnswPath, vectors, hnsw: true);
                    BuildIndex(s_flatPath, vectors, hnsw: false);
                    s_hnswSearcher = new LeanIndexSearcher(new LeanMMapDirectory(s_hnswPath));
                    s_flatSearcher = new LeanIndexSearcher(new LeanMMapDirectory(s_flatPath));
                    s_referenceVectors = vectors;
                    s_lastKey = key;
                    s_built = true;
                }
            }
        }

        _query = new float[Dimension];
        // Keep the query deterministic but independent from the vector fixture.
        // Reusing seed 7 made the query identical to document zero.
        var queryRandom = new Random(17);
        for (int dimension = 0; dimension < Dimension; dimension++)
            _query[dimension] = (float)(queryRandom.NextDouble() * 2 - 1);

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

        var hnsw = s_hnswSearcher.Search(
            new LeanVectorQuery("emb", _query, topK: 10, efSearch: EfSearch), 10);
        if (hnsw.ScoreDocs.Length != 10)
            throw new InvalidOperationException($"Expected ten HNSW results, got {hnsw.ScoreDocs.Length}.");
        int recallHits = hnsw.ScoreDocs.Count(scoreDoc =>
            _flatTopDocumentIds.Contains(scoreDoc.DocId));
        if (recallHits == 0)
            throw new InvalidOperationException("HNSW recall is zero against the exact top-ten reference.");
        _hnswChecksum = ResultChecksum(hnsw);
    }

    private static void BuildIndex(string path, float[][] vectors, bool hnsw)
    {
        var config = new LeanIndexWriterConfig
        {
            BuildHnswOnFlush = hnsw,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1L,
        };

        using var writer = new LeanIndexWriter(new LeanMMapDirectory(path), config);
        for (int i = 0; i < vectors.Length; i++)
        {
            var document = new LeanDocument();
            document.Add(new VectorField("emb", new ReadOnlyMemory<float>(vectors[i])));
            writer.AddDocument(document);
        }
        writer.Commit();
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
            new LeanVectorQuery("emb", _query, topK: 10, efSearch: EfSearch), 10);
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
        var heap = new (float Similarity, int DocId)[topK];
        int heapSize = 0;
        float queryNorm = QueryNorm(_query);

        for (int i = 0; i < s_referenceVectors.Length; i++)
        {
            var vector = s_referenceVectors[i];
            float vectorNorm = 0f;
            float dot = 0f;
            for (int dimension = 0; dimension < Dimension; dimension++)
            {
                vectorNorm += vector[dimension] * vector[dimension];
                dot += vector[dimension] * _query[dimension];
            }
            dot /= MathF.Sqrt(vectorNorm * queryNorm);

            if (heapSize < topK || dot > heap[0].Similarity)
            {
                if (heapSize < topK)
                {
                    heap[heapSize++] = (dot, i);
                    SiftUp(heap, heapSize - 1);
                }
                else
                {
                    heap[0] = (dot, i);
                    SiftDown(heap, heapSize);
                }
            }
        }

        Array.Sort(heap, 0, heapSize, Comparer<(float Similarity, int DocId)>.Create(
            static (left, right) =>
            {
                int score = right.Similarity.CompareTo(left.Similarity);
                return score != 0 ? score : left.DocId.CompareTo(right.DocId);
            }));
        var documentIds = new int[heapSize];
        for (int i = 0; i < heapSize; i++)
            documentIds[i] = heap[i].DocId;
        return documentIds;
    }

    /// <summary>Release shared resources after all HNSW parameter rows complete.</summary>
    public static void CleanupLuceneResources() => DisposeStaticResources();

    [GlobalCleanup]
    public void Cleanup() => DisposeStaticResources();

    private static void SiftUp((float Similarity, int DocId)[] heap, int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (heap[parent].Similarity <= heap[index].Similarity)
                break;
            (heap[parent], heap[index]) = (heap[index], heap[parent]);
            index = parent;
        }
    }

    private static void SiftDown((float Similarity, int DocId)[] heap, int size)
    {
        int index = 0;
        while (true)
        {
            int smallest = index;
            int left = 2 * index + 1;
            int right = left + 1;
            if (left < size && heap[left].Similarity < heap[smallest].Similarity)
                smallest = left;
            if (right < size && heap[right].Similarity < heap[smallest].Similarity)
                smallest = right;
            if (smallest == index)
                return;
            (heap[index], heap[smallest]) = (heap[smallest], heap[index]);
            index = smallest;
        }
    }

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

    private static float QueryNorm(float[] query)
    {
        float norm = 0f;
        for (int i = 0; i < query.Length; i++)
            norm += query[i] * query[i];
        return norm;
    }

    private static void DisposeStaticResources()
    {
        if (s_built)
        {
            s_hnswSearcher.Dispose();
            s_flatSearcher.Dispose();
            BenchmarkHelpers.DeleteDirectory(s_hnswPath);
            BenchmarkHelpers.DeleteDirectory(s_flatPath);
        }

        s_built = false;
        s_lastKey = default;
        s_hnswPath = string.Empty;
        s_flatPath = string.Empty;
        s_referenceVectors = [];
    }
}
