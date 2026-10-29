using System.Globalization;
using BenchmarkDotNet.Attributes;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexSearcherConfig = Rowles.LeanCorpus.Search.Searcher.IndexSearcherConfig;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanFuzzyQuery = Rowles.LeanCorpus.Search.Queries.FuzzyQuery;
using LeanLevenshteinAutomaton = Rowles.LeanCorpus.Codecs.Fst.LevenshteinAutomaton;
using LeanNoMergePolicy = Rowles.LeanCorpus.Index.Indexer.NoMergePolicy;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures fuzzy search and expansion stages on a fixed 100-segment index.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class FuzzyMultiSegmentBenchmarks
{
    private const int SegmentCount = 100;
    private const int TopN = 25;
    private const string FieldPrefix = "body\0";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string? _indexPath;
    private LeanMMapDirectory? _directory;
    private LeanIndexSearcher? _coldSearcher;
    private LeanIndexSearcher? _warmSearcher;
    private IReadOnlyList<Rowles.LeanCorpus.Index.Segment.SegmentReader> _readers = [];
    private LeanFuzzyQuery _noHitQuery = null!;
    private LeanFuzzyQuery _exactQuery = null!;
    private LeanLevenshteinAutomaton _intersectionAutomaton = null!;

    [GlobalSetup]
    public void Setup()
    {
        if (DocumentCount < SegmentCount)
            throw new InvalidOperationException(
                $"The segmented fuzzy fixture requires at least {SegmentCount} documents; got {DocumentCount}.");

        _indexPath = Path.Combine(
            BenchmarkHelpers.TempRoot,
            $"leancorpus-fuzzy-segments-{SegmentCount}-{Guid.NewGuid():N}");
        _directory = new LeanMMapDirectory(_indexPath);
        int documentsPerSegment = (DocumentCount + SegmentCount - 1) / SegmentCount;

        using (var writer = new LeanIndexWriter(_directory, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            MergePolicy = LeanNoMergePolicy.Instance
        }))
        {
            for (int documentId = 0; documentId < DocumentCount; documentId++)
            {
                var document = new LeanDocument();
                document.Add(new LeanStringField("id", documentId.ToString(CultureInfo.InvariantCulture)));
                document.Add(new LeanTextField(
                    "body",
                    string.Concat("government president token", documentId.ToString("D8", CultureInfo.InvariantCulture))));
                writer.AddDocument(document);

                if ((documentId + 1) % documentsPerSegment == 0 || documentId + 1 == DocumentCount)
                    writer.Commit();
            }
        }

        _coldSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = false,
            ParallelSearch = false
        });
        _warmSearcher = new LeanIndexSearcher(_directory, new LeanIndexSearcherConfig
        {
            EnableQueryCache = false,
            UseFuzzyExpansionCache = true,
            ParallelSearch = false
        });
        _readers = _coldSearcher.GetSegmentReaders();
        if (_readers.Count != SegmentCount)
            throw new InvalidOperationException(
                $"The segmented fuzzy fixture contains {_readers.Count} segments; expected {SegmentCount}.");

        _noHitQuery = new LeanFuzzyQuery("body", "zzzznomatch", maxEdits: 2, maxExpansions: 50);
        _exactQuery = new LeanFuzzyQuery("body", "government", maxEdits: 0, maxExpansions: 50);
        _intersectionAutomaton = new LeanLevenshteinAutomaton(_noHitQuery.Term, _noHitQuery.MaxEdits);

        if (_coldSearcher.Search(_noHitQuery, TopN).TotalHits != 0
            || _warmSearcher.Search(_noHitQuery, TopN).TotalHits != 0
            || _coldSearcher.Search(_exactQuery, TopN).TotalHits != DocumentCount)
        {
            throw new InvalidOperationException("The segmented fuzzy benchmark fixture failed its hit-count preflight.");
        }

        int intersectionMatches = _readers.Sum(reader =>
            reader.IntersectAutomaton(FieldPrefix, _intersectionAutomaton).Count);
        if (intersectionMatches != 0)
            throw new InvalidOperationException(
                $"The no-hit FST preflight found {intersectionMatches} terms; expected zero.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _warmSearcher?.Dispose();
        _coldSearcher?.Dispose();
        _directory?.Dispose();
        if (_indexPath is not null && Directory.Exists(_indexPath))
            Directory.Delete(_indexPath, recursive: true);
    }

    [Benchmark(Baseline = true, Description = "Cold edit2 no-hit search, 100 segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ColdNoHitSearch()
        => _coldSearcher!.Search(_noHitQuery, TopN).TotalHits;

    [Benchmark(Description = "Warm empty expansion cache, 100 segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int WarmNoHitSearch()
        => _warmSearcher!.Search(_noHitQuery, TopN).TotalHits;

    [Benchmark(Description = "Exact edit0 search, 100 segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ExactEditZeroSearch()
        => _coldSearcher!.Search(_exactQuery, TopN).TotalHits;

    [Benchmark(Description = "Compile edit2 automaton only")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CompileAutomatonOnly()
    {
        var automaton = new LeanLevenshteinAutomaton(_noHitQuery.Term, _noHitQuery.MaxEdits);
        return automaton.MinDistance(automaton.Start);
    }

    [Benchmark(Description = "FST intersection only, 100 segments")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int FstIntersectionOnly()
    {
        int matchCount = 0;
        foreach (var reader in _readers)
            matchCount += reader.IntersectAutomaton(FieldPrefix, _intersectionAutomaton).Count;
        return matchCount;
    }
}
