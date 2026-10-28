using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures stable-session lifecycle, cursor continuation, integrity protection and parallel use.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class SearchSessionBenchmarks
{
    private const int PageSize = 25;
    private string _path = string.Empty;
    private MMapDirectory? _directDirectory;
    private MMapDirectory? _unsignedDirectory;
    private MMapDirectory? _signedDirectory;
    private IndexSearcher? _directSearcher;
    private SearcherManager? _unsignedSearchers;
    private SearcherManager? _signedSearchers;
    private SearchSessionManager? _unsignedSessions;
    private SearchSessionManager? _signedSessions;
    private SearchSession? _unsignedSession;
    private SearchSession? _signedSession;
    private readonly MatchAllDocsQuery _query = new();
    private readonly SortField[] _multiFieldSort = [SortField.Numeric("rank", descending: true), SortField.String("category"), SortField.DocId];
    private string _unsignedCursor = string.Empty;
    private string _signedCursor = string.Empty;
    private string _multiFieldCursor = string.Empty;
    private ScoreDoc _directAfter;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.Combine(BenchmarkHelpers.TempRoot, $"sessions-{Guid.NewGuid():N}");
        RecentFeatureBenchmarkIndex.Build(_path, BenchmarkData.BuildDocuments(DocumentCount));
        _directDirectory = new MMapDirectory(_path);
        _unsignedDirectory = new MMapDirectory(_path);
        _signedDirectory = new MMapDirectory(_path);
        _directSearcher = new IndexSearcher(_directDirectory);
        _unsignedSearchers = new SearcherManager(_unsignedDirectory);
        _signedSearchers = new SearcherManager(_signedDirectory);
        _unsignedSessions = new SearchSessionManager(_unsignedSearchers, CreateOptions(key: null));
        _signedSessions = new SearchSessionManager(_signedSearchers, CreateOptions(Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray()));
        _unsignedSession = _unsignedSessions.OpenSession();
        _signedSession = _signedSessions.OpenSession();

        var unsignedFirst = _unsignedSession.Search(_query, PageSize);
        var signedFirst = _signedSession.Search(_query, PageSize);
        var multiFieldFirst = _signedSession.Search(_query, PageSize, sorts: _multiFieldSort);
        _unsignedCursor = unsignedFirst.NextCursor!;
        _signedCursor = signedFirst.NextCursor!;
        _multiFieldCursor = multiFieldFirst.NextCursor!;
        _directAfter = _directSearcher.Search(_query, PageSize).ScoreDocs[^1];
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _unsignedSession?.Dispose();
        _signedSession?.Dispose();
        _unsignedSessions?.Dispose();
        _signedSessions?.Dispose();
        _unsignedSearchers?.Dispose();
        _signedSearchers?.Dispose();
        _directSearcher?.Dispose();
        RecentFeatureBenchmarkIndex.Delete(_path);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int DirectSearch() => _directSearcher!.Search(_query, PageSize).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int UnsignedSession_FirstPage() => _unsignedSession!.Search(_query, PageSize).Results.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SignedSession_FirstPage() => _signedSession!.Search(_query, PageSize).Results.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int DirectSearchAfter()
        => _directSearcher!.SearchAfter(_directAfter, _query, PageSize, SortField.Score).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int UnsignedSession_Continuation()
        => _unsignedSession!.Search(_query, PageSize, _unsignedCursor).Results.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SignedSession_Continuation()
        => _signedSession!.Search(_query, PageSize, _signedCursor).Results.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SignedSession_MultiFieldContinuation()
        => _signedSession!.Search(_query, PageSize, _multiFieldCursor, _multiFieldSort).Results.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int OpenAndCloseSession()
    {
        using var session = _unsignedSessions!.OpenSession();
        return session.Id.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int SessionDiagnostics()
    {
        var diagnostics = _unsignedSessions!.GetDiagnostics();
        return diagnostics.ActiveSessions + diagnostics.RetainedGenerations.Count;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ParallelSignedContinuations()
    {
        int hits = 0;
        Parallel.For(0, 4, _ => Interlocked.Add(ref hits,
            _signedSession!.Search(_query, PageSize, _signedCursor).Results.TotalHits));
        return hits;
    }

    private static SearchSessionOptions CreateOptions(byte[]? key) => new()
    {
        MaximumLifetime = TimeSpan.FromHours(1),
        MaximumConcurrentSessions = 128,
        MaximumRetainedGenerations = 8,
        MaximumRetainedBytes = long.MaxValue,
        CursorIntegrityKey = key
    };
}
