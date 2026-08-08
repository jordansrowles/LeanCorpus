using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures federated reader construction, search, pagination, faceting and ordinal composition.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class MultiReaderBenchmarks
{
    private const int TopN = 25;

    [Params(1, 4, 16)]
    public int ReaderCount { get; set; }

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _paths = [];
    private MMapDirectory[] _directories = [];
    private string _singlePath = string.Empty;
    private MMapDirectory? _singleDirectory;
    private MultiReader? _multiReader;
    private IndexSearcher? _singleSearcher;
    private readonly MatchAllDocsQuery _query = new();
    private ScoreDoc _multiAfter;
    private ScoreDoc _singleAfter;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        _paths = new string[ReaderCount];
        _directories = new MMapDirectory[ReaderCount];
        int offset = 0;
        for (int reader = 0; reader < ReaderCount; reader++)
        {
            int remainingReaders = ReaderCount - reader;
            int count = (documents.Length - offset + remainingReaders - 1) / remainingReaders;
            var shard = documents.AsSpan(offset, count).ToArray();
            _paths[reader] = Path.Combine(BenchmarkHelpers.TempRoot, $"multi-{reader}-{Guid.NewGuid():N}");
            RecentFeatureBenchmarkIndex.Build(_paths[reader], shard, documentOffset: offset);
            _directories[reader] = new MMapDirectory(_paths[reader]);
            offset += count;
        }

        _singlePath = Path.Combine(BenchmarkHelpers.TempRoot, $"multi-single-{Guid.NewGuid():N}");
        RecentFeatureBenchmarkIndex.Build(_singlePath, documents);
        _singleDirectory = new MMapDirectory(_singlePath);
        _multiReader = new MultiReader(_directories);
        _singleSearcher = new IndexSearcher(_singleDirectory);
        _multiAfter = _multiReader.Search(_query, TopN).ScoreDocs[^1];
        _singleAfter = _singleSearcher.Search(_query, TopN).ScoreDocs[^1];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _multiReader?.Dispose();
        _singleSearcher?.Dispose();
        foreach (string path in _paths)
            RecentFeatureBenchmarkIndex.Delete(path);
        RecentFeatureBenchmarkIndex.Delete(_singlePath);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int OpenMultiReader()
    {
        using var reader = new MultiReader(_directories);
        return reader.MaxDoc;
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SingleIndexSearch() => _singleSearcher!.Search(_query, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FederatedSearch() => _multiReader!.Search(_query, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SingleIndexContinuation()
        => _singleSearcher!.SearchAfter(_singleAfter, _query, TopN, SortField.Score).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FederatedContinuation()
        => _multiReader!.SearchAfter(_multiAfter, _query, TopN, SortField.Score).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FederatedFacets()
    {
        var (results, facets) = _multiReader!.SearchWithFacets(_query, TopN, "category");
        return results.TotalHits + facets.Sum(static facet => facet.Buckets.Count);
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int BuildGlobalOrdinalMap() => _multiReader!.GetOrdinalMap("category", sortedSet: true).ValueCount;
}
