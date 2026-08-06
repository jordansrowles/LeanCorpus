using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures compound segment creation, opening and common read paths against loose segment files.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class CompoundFileBenchmarks
{
    private const int TopN = 25;
    private string[] _documents = [];
    private string _loosePath = string.Empty;
    private string _compoundPath = string.Empty;
    private string _looseVectorPath = string.Empty;
    private string _compoundVectorPath = string.Empty;
    private IndexSearcher? _looseSearcher;
    private IndexSearcher? _compoundSearcher;
    private IndexSearcher? _looseVectorSearcher;
    private IndexSearcher? _compoundVectorSearcher;
    private readonly TermQuery _query = new("body", "government");
    private readonly MatchAllDocsQuery _matchAll = new();
    private float[] _queryVector = [];

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _loosePath = Path.Combine(BenchmarkHelpers.TempRoot, $"compound-loose-{Guid.NewGuid():N}");
        _compoundPath = Path.Combine(BenchmarkHelpers.TempRoot, $"compound-cfs-{Guid.NewGuid():N}");
        _looseVectorPath = Path.Combine(BenchmarkHelpers.TempRoot, $"compound-vector-loose-{Guid.NewGuid():N}");
        _compoundVectorPath = Path.Combine(BenchmarkHelpers.TempRoot, $"compound-vector-cfs-{Guid.NewGuid():N}");
        RecentFeatureBenchmarkIndex.Build(_loosePath, _documents);
        RecentFeatureBenchmarkIndex.Build(_compoundPath, _documents, useCompoundFile: true);
        BuildVectorIndex(_looseVectorPath, useCompoundFile: false);
        BuildVectorIndex(_compoundVectorPath, useCompoundFile: true);
        _looseSearcher = new IndexSearcher(new MMapDirectory(_loosePath));
        _compoundSearcher = new IndexSearcher(new MMapDirectory(_compoundPath));
        _looseVectorSearcher = new IndexSearcher(new MMapDirectory(_looseVectorPath));
        _compoundVectorSearcher = new IndexSearcher(new MMapDirectory(_compoundVectorPath));
        _queryVector = CreateVector(0);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _looseSearcher?.Dispose();
        _compoundSearcher?.Dispose();
        _looseVectorSearcher?.Dispose();
        _compoundVectorSearcher?.Dispose();
        RecentFeatureBenchmarkIndex.Delete(_loosePath);
        RecentFeatureBenchmarkIndex.Delete(_compoundPath);
        RecentFeatureBenchmarkIndex.Delete(_looseVectorPath);
        RecentFeatureBenchmarkIndex.Delete(_compoundVectorPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LooseFiles_IndexAndCommit() => BuildAndDelete(useCompoundFile: false);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundFile_IndexAndCommit() => BuildAndDelete(useCompoundFile: true);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LooseFiles_IndexCommitUnderMergePressure() => BuildUnderMergePressureAndDelete(useCompoundFile: false);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundFile_IndexCommitUnderMergePressure() => BuildUnderMergePressureAndDelete(useCompoundFile: true);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LooseFiles_OpenReader()
    {
        using var searcher = new IndexSearcher(new MMapDirectory(_loosePath));
        return searcher.Stats.TotalDocCount;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundFile_OpenReader()
    {
        using var searcher = new IndexSearcher(new MMapDirectory(_compoundPath));
        return searcher.Stats.TotalDocCount;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LooseFiles_Search() => _looseSearcher!.Search(_query, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundFile_Search() => _compoundSearcher!.Search(_query, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LooseFiles_StoredFields()
        => ReadStoredFields(_looseSearcher!);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundFile_StoredFields()
        => ReadStoredFields(_compoundSearcher!);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LooseFiles_VectorSearch()
        => _looseVectorSearcher!.Search(new VectorQuery("embedding", _queryVector, TopN, efSearch: 64), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundFile_VectorSearch()
        => _compoundVectorSearcher!.Search(new VectorQuery("embedding", _queryVector, TopN, efSearch: 64), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LooseFiles_DocValuesAndFacets()
    {
        var (results, facets) = _looseSearcher!.SearchWithFacets(_query, TopN, "category");
        return results.TotalHits + facets.Sum(static facet => facet.Buckets.Count);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompoundFile_DocValuesAndFacets()
    {
        var (results, facets) = _compoundSearcher!.SearchWithFacets(_query, TopN, "category");
        return results.TotalHits + facets.Sum(static facet => facet.Buckets.Count);
    }

    private int BuildAndDelete(bool useCompoundFile)
    {
        string path = Path.Combine(BenchmarkHelpers.TempRoot, $"compound-index-{Guid.NewGuid():N}");
        try
        {
            RecentFeatureBenchmarkIndex.Build(path, _documents, useCompoundFile);
            return Directory.EnumerateFiles(path).Count();
        }
        finally
        {
            RecentFeatureBenchmarkIndex.Delete(path);
        }
    }

    private int BuildUnderMergePressureAndDelete(bool useCompoundFile)
    {
        string path = Path.Combine(BenchmarkHelpers.TempRoot, $"compound-merge-{Guid.NewGuid():N}");
        try
        {
            RecentFeatureBenchmarkIndex.Build(path, _documents, useCompoundFile, maxBufferedDocs: 100);
            return Directory.EnumerateFiles(path).Count();
        }
        finally
        {
            RecentFeatureBenchmarkIndex.Delete(path);
        }
    }

    private int ReadStoredFields(IndexSearcher searcher)
    {
        var results = searcher.Search(_matchAll, TopN);
        int values = 0;
        foreach (var hit in results.ScoreDocs)
            values += searcher.GetStoredFields(hit.DocId).Count;
        return values;
    }

    private void BuildVectorIndex(string path, bool useCompoundFile)
    {
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxBufferedDocs = 1_000,
            RamBufferSizeMB = 256,
            UseCompoundFile = useCompoundFile,
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswSeed = 1L
        });
        int vectorDocumentCount = Math.Min(DocumentCount, 10_000);
        for (int i = 0; i < vectorDocumentCount; i++)
        {
            var document = new LeanDocument();
            document.Add(new VectorField("embedding", CreateVector(i)));
            writer.AddDocument(document);
        }
        writer.Commit();
    }

    private static float[] CreateVector(int documentId)
    {
        var vector = new float[64];
        for (int dimension = 0; dimension < vector.Length; dimension++)
            vector[dimension] = MathF.Sin((documentId + 1) * (dimension + 1) * 0.017f);
        return vector;
    }
}
