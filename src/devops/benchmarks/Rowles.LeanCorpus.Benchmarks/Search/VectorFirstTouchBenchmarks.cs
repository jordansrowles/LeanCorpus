using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures serial and parallel cold first-touch of independent vector/HNSW fields.</summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(2)]
[IterationCount(5)]
public class VectorFirstTouchBenchmarks
{
    private const int DocumentCount = 4096;
    private const int Dimension = 64;
    private const int TopK = 5;
    private const int EfSearch = 64;

    [Params(1, 4)]
    public int VectorFieldCount { get; set; }

    private string _indexPath = string.Empty;
    private float[][] _queryVectors = [];
    private int[][] _expectedResults = [];

    [GlobalSetup]
    public void Setup()
    {
        _indexPath = Path.Combine(BenchmarkHelpers.TempRoot,
            $"leancorpus-bench-vector-first-touch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_indexPath);
        _queryVectors = new float[VectorFieldCount][];

        using (var directory = new MMapDirectory(_indexPath))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            DurableCommits = false,
            HnswBuildConfig = new HnswBuildConfig { M = 8, M0 = 16, EfConstruction = 40 },
            HnswSeed = 1L,
            MaxBufferedDocs = DocumentCount + 1,
            MergePolicy = NoMergePolicy.Instance,
            NormaliseVectors = true,
        }))
        {
            var random = new Random(20260928);
            for (int docId = 0; docId < DocumentCount; docId++)
            {
                var document = new LeanDocument();
                for (int field = 0; field < VectorFieldCount; field++)
                {
                    var vector = new float[Dimension];
                    for (int dimension = 0; dimension < vector.Length; dimension++)
                        vector[dimension] = (random.NextSingle() * 2f) - 1f;

                    if (docId == 0)
                        _queryVectors[field] = (float[])vector.Clone();
                    document.Add(new VectorField(FieldName(field), vector));
                }
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        _expectedResults = new int[VectorFieldCount][];
        using var validationDirectory = new MMapDirectory(_indexPath);
        using var validationSearcher = OpenSearcher(validationDirectory);
        for (int field = 0; field < VectorFieldCount; field++)
        {
            var result = SearchField(validationSearcher, field);
            _expectedResults[field] = result.ScoreDocs
                .Select(static scoreDoc => scoreDoc.DocId)
                .ToArray();
        }
    }

    [GlobalCleanup]
    public void Cleanup() => BenchmarkHelpers.DeleteDirectory(_indexPath);

    [Benchmark(Baseline = true, Description = "Serial cold vector/HNSW first-touch")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SerialFirstTouch() => RunFirstTouch(parallel: false);

    [Benchmark(Description = "Parallel cold vector/HNSW first-touch")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ParallelFirstTouch() => RunFirstTouch(parallel: true);

    private int RunFirstTouch(bool parallel)
    {
        using var directory = new MMapDirectory(_indexPath);
        using var searcher = OpenSearcher(directory);
        var results = new int[VectorFieldCount][];

        if (parallel)
        {
            Parallel.For(0, VectorFieldCount, field =>
            {
                results[field] = SearchField(searcher, field).ScoreDocs
                    .Select(static scoreDoc => scoreDoc.DocId)
                    .ToArray();
            });
        }
        else
        {
            for (int field = 0; field < VectorFieldCount; field++)
            {
                results[field] = SearchField(searcher, field).ScoreDocs
                    .Select(static scoreDoc => scoreDoc.DocId)
                    .ToArray();
            }
        }

        int checksum = 17;
        for (int field = 0; field < VectorFieldCount; field++)
        {
            if (!results[field].SequenceEqual(_expectedResults[field]))
                throw new InvalidOperationException($"Cold first-touch results changed for field '{FieldName(field)}'.");
            foreach (int docId in results[field])
                checksum = unchecked((checksum * 31) + docId);
        }
        return checksum;
    }

    private TopDocs SearchField(IndexSearcher searcher, int field)
    {
        var result = searcher.Search(
            new VectorQuery(FieldName(field), _queryVectors[field], topK: TopK, efSearch: EfSearch),
            TopK);
        if (result.ScoreDocs.Length != TopK)
            throw new InvalidOperationException($"Expected {TopK} vector hits for '{FieldName(field)}', got {result.ScoreDocs.Length}.");
        return result;
    }

    private IndexSearcher OpenSearcher(MMapDirectory directory)
        => new(directory, new IndexSearcherConfig
        {
            EnableQueryCache = false,
            MaxCachedSegmentReaders = 4,
            ParallelSearch = false,
        });

    private static string FieldName(int field) => $"embedding-{field}";
}
