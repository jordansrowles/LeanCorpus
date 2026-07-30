using BenchmarkDotNet.Attributes;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
/// Int8 (scalar quantisation), BBQ (binary quantisation), Int4, and experimental product quantisation.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class VectorQuantisationBenchmarks
{
    /// <summary>Document count, overridden by the runner's <c>--doccount</c> option when supplied.</summary>
    public int DocCount { get; set; } = 10_000;

    [Params(64, 128)]
    public int Dimension { get; set; }

    /// <summary>Quantisation strategy applied at index time.</summary>
    [Params(
        VectorQuantisation.None,
        VectorQuantisation.Int8,
        VectorQuantisation.BBQ,
        VectorQuantisation.Int4)]
    public VectorQuantisation Quantisation { get; set; }

    private const string FieldName = "emb";
    private const int TopK = 10;

    // Index state — guarded by (DocCount, Dimension, Quantisation) key
    private static readonly System.Threading.Lock s_gate = new();
    private static (int docCount, int dim, VectorQuantisation q) s_lastKey;
    private static bool s_built;
    private static string s_indexPath = string.Empty;
    private static LeanIndexSearcher s_searcher = default!;
    private static float[][] s_vectors = [];
    private static float[][] s_productQuantisedVectors = [];
    private static TimeSpan s_buildElapsed;
    private static long s_indexBytes;

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
        if (int.TryParse(Environment.GetEnvironmentVariable("BENCH_DOC_COUNT"), out int configuredDocCount) &&
            configuredDocCount > 0)
        {
            DocCount = configuredDocCount;
        }
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

                    var buildStopwatch = Stopwatch.StartNew();
                    using (var writer = new LeanIndexWriter(
                        new LeanMMapDirectory(s_indexPath), cfg))
                    {
                        for (int i = 0; i < vectors.Length; i++)
                        {
                            var doc = new LeanDocument();
                            doc.Add(new VectorField(FieldName,
                                new ReadOnlyMemory<float>(vectors[i])));
                            writer.AddDocument(doc);
                        }
                        writer.Commit();
                    }
                    buildStopwatch.Stop();

                    s_searcher = new LeanIndexSearcher(
                        new LeanMMapDirectory(s_indexPath));

                    EnsureLuceneIndex(vectors);

                    s_vectors = vectors;
                    s_productQuantisedVectors = Quantisation == VectorQuantisation.ProductQuantisation
                        ? ReadProductQuantisedVectors(s_searcher, vectors.Length)
                        : [];
                    s_buildElapsed = buildStopwatch.Elapsed;
                    s_indexBytes = Directory.EnumerateFiles(
                        s_indexPath,
                        "*",
                        SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
                    s_lastKey = key;
                    s_built = true;
                }
            }
        }

        _query = new float[Dimension];
        var qrnd = new Random(7);
        for (int d = 0; d < Dimension; d++)
            _query[d] = (float)(qrnd.NextDouble() * 2 - 1);

        WriteQualityArtefact();
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

    private void WriteQualityArtefact()
    {
        string? runDirectory = Environment.GetEnvironmentVariable("BENCH_RUN_DIRECTORY");
        if (string.IsNullOrWhiteSpace(runDirectory) || s_vectors.Length == 0)
            return;

        const int queryCount = 16;
        var random = new Random(19);
        double recallAt10 = 0d;
        double reciprocalRankAgreement = 0d;
        double absoluteScoreError = 0d;
        double codebookRecallAt10 = 0d;
        double highEfRecallAt10 = 0d;
        int scoredHits = 0;
        for (int queryIndex = 0; queryIndex < queryCount; queryIndex++)
        {
            var query = new float[Dimension];
            for (int dimension = 0; dimension < query.Length; dimension++)
                query[dimension] = (float)(random.NextDouble() * 2d - 1d);

            var exact = s_vectors
                .Select((vector, docId) => new ExactHit(
                    docId,
                    VectorQuery.CosineSimilarity(vector, query)))
                .OrderByDescending(hit => hit.Score)
                .ThenBy(hit => hit.DocId)
                .Take(TopK)
                .ToArray();
            var actual = s_searcher.Search(
                new VectorQuery(FieldName, query, topK: TopK, efSearch: 64),
                TopK).ScoreDocs;
            var exactRanks = exact
                .Select((hit, rank) => (hit.DocId, Rank: rank + 1, hit.Score))
                .ToDictionary(hit => hit.DocId);
            int overlap = 0;
            foreach (var actualHit in actual)
            {
                if (!exactRanks.TryGetValue(actualHit.DocId, out var exactHit))
                    continue;
                overlap++;
                reciprocalRankAgreement += 1d / exactHit.Rank;
                absoluteScoreError += Math.Abs(actualHit.Score - exactHit.Score);
                scoredHits++;
            }
            recallAt10 += (double)overlap / TopK;

            if (Quantisation == VectorQuantisation.ProductQuantisation)
            {
                // This is an exhaustive scan over the reconstructed PQ codebooks. It removes
                // graph traversal from the measurement, leaving codebook distortion and final
                // quantised scoring as the only quality loss.
                int[] codebookTop = FindTopKByCosine(s_productQuantisedVectors, query, TopK);
                codebookRecallAt10 += RecallAtTopK(exactRanks, codebookTop);

                var highEf = s_searcher.Search(
                    new VectorQuery(FieldName, query, topK: TopK, efSearch: 512), TopK).ScoreDocs;
                highEfRecallAt10 += RecallAtTopK(exactRanks, highEf.Select(hit => hit.DocId));
            }
        }

        var artefact = new VectorQuantisationGateArtefact(
            DocCount,
            Dimension,
            Quantisation.ToString(),
            queryCount,
            recallAt10 / queryCount,
            reciprocalRankAgreement / queryCount,
            scoredHits == 0 ? 0d : absoluteScoreError / scoredHits,
            s_indexBytes,
            s_buildElapsed.TotalMilliseconds,
            Quantisation == VectorQuantisation.ProductQuantisation
                ? codebookRecallAt10 / queryCount
                : null,
            Quantisation == VectorQuantisation.ProductQuantisation
                ? highEfRecallAt10 / queryCount
                : null);
        string fileName = $"vector-quantisation-gate-{DocCount}-{Dimension}-{Quantisation}.json";
        File.WriteAllText(
            Path.Combine(runDirectory, fileName),
            JsonSerializer.Serialize(artefact, new JsonSerializerOptions { WriteIndented = true }));
    }

    private readonly record struct ExactHit(int DocId, float Score);

    private static float[][] ReadProductQuantisedVectors(LeanIndexSearcher searcher, int documentCount)
    {
        var vectors = new float[documentCount][];
        foreach (var reader in searcher.GetSegmentReaders())
        {
            for (int docId = 0; docId < reader.MaxDoc; docId++)
            {
                float[]? vector = reader.GetVector(FieldName, docId);
                if (vector is not null)
                    vectors[reader.DocBase + docId] = vector;
            }
        }
        return vectors;
    }

    private static int[] FindTopKByCosine(float[][] vectors, ReadOnlySpan<float> query, int topK)
    {
        var heap = new PriorityQueue<int, float>(topK);
        for (int docId = 0; docId < vectors.Length; docId++)
        {
            float[]? vector = vectors[docId];
            if (vector is null)
                continue;
            float score = VectorQuery.CosineSimilarity(vector, query);
            if (heap.Count < topK)
            {
                heap.Enqueue(docId, score);
            }
            else if (heap.TryPeek(out _, out float minimumScore) && score > minimumScore)
            {
                heap.Dequeue();
                heap.Enqueue(docId, score);
            }
        }
        return heap.UnorderedItems.Select(item => item.Element).ToArray();
    }

    private static double RecallAtTopK(
        IReadOnlyDictionary<int, (int DocId, int Rank, float Score)> exactRanks,
        IEnumerable<int> actualDocIds)
    {
        int matches = actualDocIds.Count(exactRanks.ContainsKey);
        return (double)matches / TopK;
    }

    private sealed record VectorQuantisationGateArtefact(
        int DocumentCount,
        int Dimension,
        string Quantisation,
        int QueryCount,
        double RecallAt10,
        double ReciprocalRankAgreementAt10,
        double MeanAbsoluteReturnedScoreError,
        long IndexBytes,
        double BuildMilliseconds,
        double? ExhaustiveCodebookRecallAt10,
        double? HnswEf512RecallAt10);

    [GlobalCleanup]
    public void Cleanup()
    {
        // Static resources persist for class lifetime.
    }
}
