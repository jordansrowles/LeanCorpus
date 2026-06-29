using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneFvh = Lucene.Net.Search.VectorHighlight;
using Rowles.LeanCorpus.Codecs.TermVectors;
using LuceneHighlight = Lucene.Net.Search.Highlight;
using Rowles.LeanCorpus.Search.Highlighting;
using LuceneTerm = Lucene.Net.Index.Term;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares term-vector and re-analysis highlighter throughput between
/// LeanCorpus and Lucene.NET on the same index content.
/// </summary>
/// <remarks>
/// LeanCorpus <see cref="TermVectorHighlighter"/> is compared against
/// Lucene.NET <see cref="LuceneFvh.FastVectorHighlighter"/>, and the
/// re-analysis <see cref="Highlighter"/> is compared against
/// Lucene.NET <see cref="LuceneHighlight.Highlighter"/>.
/// </remarks>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class TermVectorHighlighterBenchmarks
{
    private const int MaxSnippetLength = 200;
    private const int SampleCount = 100;
    private const int TopN = 100;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    // LeanCorpus state
    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private Index.Segment.SegmentReader[] _leanSegmentReaders = [];
    private TermVectorHighlighter _leanTvHighlighter = null!;
    private Highlighter _leanHighlighter = null!;
    private HybridHighlighter _leanHybridHighlighter = null!;

    // Lucene.NET state
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private Lucene.Net.Index.DirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;
    private StandardAnalyzer _luceneAnalyzer = null!;

    // Pre-extracted data
    private (string Text, IReadOnlyList<TermVectorEntry> TVs)[] _leanTvSamples = [];
    private FieldQuery _leanTvFieldQuery = null!;
    private int[] _luceneFvhDocIds = [];
    private LuceneFvh.FieldQuery _luceneFvhFieldQuery = null!;
    private (string Text, IReadOnlySet<string> QueryTerms)[] _leanReanalysisSamples = [];
    private (string Text, Lucene.Net.Search.Query Query)[] _luceneReanalysisSamples = [];
    private (string Text, IReadOnlyList<TermVectorEntry> TVs)[] _leanHybridNoOffsetsSamples = [];

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
        BuildLuceneIndex(documents);
        ExtractSamples();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        foreach (var reader in _leanSegmentReaders)
            reader.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    // -----------------------------------------------------------------------
    // Term-vector path (offset-based)
    // -----------------------------------------------------------------------

    [Benchmark]
    [BenchmarkCategory("highlighter")]
    [BenchmarkCategory("tv")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TermVectorHighlighter()
    {
        int total = 0;
        foreach (var (text, tvs) in _leanTvSamples)
            total += _leanTvHighlighter.GetBestFragment(text, _leanTvFieldQuery, tvs, MaxSnippetLength).Length;
        return total;
    }

    [Benchmark]
    [BenchmarkCategory("highlighter")]
    [BenchmarkCategory("tv")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_FastVectorHighlighter()
    {
        var fvh = new LuceneFvh.FastVectorHighlighter();
        int total = 0;
        foreach (var docId in _luceneFvhDocIds)
        {
            var fragment = fvh.GetBestFragment(_luceneFvhFieldQuery, _luceneReader!, docId, "body", MaxSnippetLength);
            if (fragment is not null)
                total += fragment.Length;
        }
        return total;
    }

    // -----------------------------------------------------------------------
    // Re-analysis path
    // -----------------------------------------------------------------------

    [Benchmark]
    [BenchmarkCategory("highlighter")]
    [BenchmarkCategory("reanalysis")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Highlighter()
    {
        int total = 0;
        foreach (var (text, queryTerms) in _leanReanalysisSamples)
            total += _leanHighlighter.GetBestFragment(text, queryTerms, MaxSnippetLength).Length;
        return total;
    }

    [Benchmark]
    [BenchmarkCategory("highlighter")]
    [BenchmarkCategory("reanalysis")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Highlighter()
    {
        int total = 0;
        foreach (var (text, query) in _luceneReanalysisSamples)
        {
            var scorer = new LuceneHighlight.QueryScorer(query);
            var formatter = new LuceneHighlight.SimpleHTMLFormatter("<b>", "</b>");
            var hl = new LuceneHighlight.Highlighter(formatter, scorer);
            var fragment = hl.GetBestFragment(_luceneAnalyzer, "body", text);
            if (fragment is not null)
                total += fragment.Length;
        }
        return total;
    }

    // -----------------------------------------------------------------------
    // Hybrid fallback path (no offsets — LeanCorpus only)
    // -----------------------------------------------------------------------

    [Benchmark]
    [BenchmarkCategory("highlighter")]
    [BenchmarkCategory("hybrid")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_HybridHighlighter_NoOffsets()
    {
        var query = new TermQuery("body", "government");
        int total = 0;
        foreach (var (text, tvs) in _leanHybridNoOffsetsSamples)
            total += _leanHybridHighlighter.GetBestFragment(text, query, tvs, MaxSnippetLength).Length;
        return total;
    }

    // -----------------------------------------------------------------------
    // Index builders
    // -----------------------------------------------------------------------

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-tvh-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new IndexWriter(
            _leanDirectory,
            new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256,
                StoreTermVectors = true
            });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);

        // Build segment readers for direct term vector access.
        var segFiles = IODirectory.GetFiles(_leanIndexPath, "*.seg");
        Array.Sort(segFiles, static (a, b) => string.CompareOrdinal(
            Path.GetFileNameWithoutExtension(a),
            Path.GetFileNameWithoutExtension(b)));
        _leanSegmentReaders = new Index.Segment.SegmentReader[segFiles.Length];
        for (int i = 0; i < segFiles.Length; i++)
        {
            var info = Index.Segment.SegmentInfo.ReadFrom(segFiles[i]);
            _leanSegmentReaders[i] = new Index.Segment.SegmentReader(_leanDirectory, info);
        }

        _leanTvHighlighter = new TermVectorHighlighter("<b>", "</b>");
        _leanHighlighter = new Highlighter("<b>", "</b>");
        _leanHybridHighlighter = new HybridHighlighter("<b>", "</b>");
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-tvh-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);

        _luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer));
        var fieldType = new Lucene.Net.Documents.FieldType
        {
            IsIndexed = true,
            IsTokenized = true,
            IsStored = false,
            StoreTermVectors = true,
            StoreTermVectorPositions = true,
            StoreTermVectorOffsets = true,
            StoreTermVectorPayloads = false
        };
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new Lucene.Net.Documents.Document
            {
                new Lucene.Net.Documents.StringField(
                    "id",
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Lucene.Net.Documents.Field.Store.NO),
                new Lucene.Net.Documents.Field("body", documents[i], fieldType)
            };
            writer.AddDocument(doc);
        }
        writer.Commit();

        _luceneReader = Lucene.Net.Index.DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

    // -----------------------------------------------------------------------
    // Sample extraction
    // -----------------------------------------------------------------------

    private void ExtractSamples()
    {
        // Build FieldQuery instances for the TV path.
        _leanTvFieldQuery = new FieldQuery(new TermQuery("body", "government"));

        var lq = new LuceneTermQuery(new LuceneTerm("body", "government"));
        var fvh = new LuceneFvh.FastVectorHighlighter();
        _luceneFvhFieldQuery = fvh.GetFieldQuery(lq);

        // Search for matching docs using LeanCorpus searcher.
        var results = _leanSearcher!.Search(new TermQuery("body", "government"), TopN);
        int sampleCount = Math.Min(SampleCount, results.ScoreDocs.Length);

        if (sampleCount == 0)
        {
            var fbQueryTerms = new HashSet<string>(new[] { "government" }, StringComparer.OrdinalIgnoreCase);
            // No matching docs for the query term; use first docs from the index as-is.
            sampleCount = Math.Min(SampleCount, _leanSegmentReaders.Sum(r => r.MaxDoc));
            for (int i = 0; i < sampleCount; i++)
            {
                var reader = _leanSegmentReaders[0];
                int localDocId = i < reader.MaxDoc ? i : 0;
                var stored = reader.GetStoredFields(localDocId);
                var text = stored.TryGetValue("body", out var vals) && vals.Count > 0 ? vals[0] : string.Empty;
                if (string.IsNullOrEmpty(text)) continue;
                var allTVs = reader.GetTermVectors(localDocId);
                IReadOnlyList<TermVectorEntry> bodyTVs = allTVs is not null && allTVs.TryGetValue("body", out var tvEntries)
                    ? tvEntries
                    : Array.Empty<TermVectorEntry>();
                _leanTvSamples = [.. _leanTvSamples, (text, bodyTVs)];
                _leanReanalysisSamples = [.. _leanReanalysisSamples, (text, fbQueryTerms)];
                var noOffsetTVs = bodyTVs
                    .Select(e => new TermVectorEntry(e.Term, e.Freq, e.Positions, e.Payloads, null, null))
                    .ToArray();
                _leanHybridNoOffsetsSamples = [.. _leanHybridNoOffsetsSamples, (text, noOffsetTVs)];
            }
            _luceneFvhDocIds = new int[sampleCount];
            var lucList = new List<(string Text, Lucene.Net.Search.Query Query)>(sampleCount);
            for (int i = 0; i < sampleCount; i++)
            {
                _luceneFvhDocIds[i] = i;
                var doc = _luceneReader!.Document(i);
                var ltext = doc.Get("body") ?? string.Empty;
                lucList.Add((ltext, lq));
            }
            _luceneReanalysisSamples = lucList.ToArray();
            return;
        }

        var leanTvList = new List<(string Text, IReadOnlyList<TermVectorEntry> TVs)>(sampleCount);
        var leanReanalysisList = new List<(string Text, IReadOnlySet<string> QueryTerms)>(sampleCount);
        var leanHybridNoOffsetsList = new List<(string Text, IReadOnlyList<TermVectorEntry> TVs)>(sampleCount);

        // Build doc base array for global-to-local doc ID mapping.
        var docBases = new int[_leanSegmentReaders.Length];
        int docBase = 0;
        for (int i = 0; i < _leanSegmentReaders.Length; i++)
        {
            docBases[i] = docBase;
            docBase += _leanSegmentReaders[i].MaxDoc;
        }

        var queryTerms = new HashSet<string>(new[] { "government" }, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < sampleCount; i++)
        {
            int globalDocId = results.ScoreDocs[i].DocId;

            // Map to segment + local doc ID.
            int segIdx = 0;
            int localDocId = globalDocId;
            for (int s = 0; s < docBases.Length; s++)
            {
                int nextBase = s + 1 < docBases.Length ? docBases[s + 1] : int.MaxValue;
                if (globalDocId >= docBases[s] && globalDocId < nextBase)
                {
                    segIdx = s;
                    localDocId = globalDocId - docBases[s];
                    break;
                }
            }

            var reader = _leanSegmentReaders[segIdx];
            var stored = reader.GetStoredFields(localDocId);
            var text = stored.TryGetValue("body", out var values) && values.Count > 0 ? values[0] : string.Empty;

            if (string.IsNullOrEmpty(text))
                continue;

            var allTVs = reader.GetTermVectors(localDocId);
            IReadOnlyList<TermVectorEntry> bodyTVs = allTVs is not null && allTVs.TryGetValue("body", out var entries)
                ? entries
                : Array.Empty<TermVectorEntry>();

            leanTvList.Add((text, bodyTVs));
            leanReanalysisList.Add((text, queryTerms));

            // Strip offsets for hybrid no-offsets test.
            var noOffsetTVs = bodyTVs
                .Select(e => new TermVectorEntry(e.Term, e.Freq, e.Positions, e.Payloads, null, null))
                .ToArray();
            leanHybridNoOffsetsList.Add((text, noOffsetTVs));
        }

        _leanTvSamples = leanTvList.ToArray();
        _leanReanalysisSamples = leanReanalysisList.ToArray();
        _leanHybridNoOffsetsSamples = leanHybridNoOffsetsList.ToArray();

        // Lucene.NET samples.
        var luceneResults = _luceneSearcher!.Search(lq, sampleCount);
        _luceneFvhDocIds = new int[luceneResults.ScoreDocs.Length];
        var luceneReanalysisList = new List<(string Text, Lucene.Net.Search.Query Query)>(luceneResults.ScoreDocs.Length);
        for (int i = 0; i < luceneResults.ScoreDocs.Length; i++)
        {
            int docId = luceneResults.ScoreDocs[i].Doc;
            _luceneFvhDocIds[i] = docId;

            var doc = _luceneSearcher.Doc(docId);
            var text = doc.Get("body") ?? string.Empty;

            luceneReanalysisList.Add((text, lq));
        }
        _luceneReanalysisSamples = luceneReanalysisList.ToArray();
    }
}
