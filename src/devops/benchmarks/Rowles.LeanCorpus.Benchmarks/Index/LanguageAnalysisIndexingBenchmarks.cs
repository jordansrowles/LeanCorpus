using System.Diagnostics;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures language-analysis allocations through the real DWPT indexing path
/// for short, multi-field, medium and long text workloads.
/// </summary>
/// <remarks>
/// Each workload indexes a fixed number of documents, the same for 1, 2 and 4 producers,
/// sized so every row takes well over 100 ms. Setup runs one untimed preflight per row that
/// prints stage timings, allocation, segment and flush counts, and fails the run if the
/// persisted index differs from the single-producer index.
/// </remarks>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[KeepBenchmarkFiles]
[InvocationCount(1)]
public class LanguageAnalysisIndexingBenchmarks
{
    /// <summary>Documents indexed by one operation; fixed per workload and independent of producers.</summary>
    public int DocumentCount => DocumentCountFor(Workload);

    [Params(LanguageAnalysisWorkload.ShortSingleField, LanguageAnalysisWorkload.ManyShortFields,
        LanguageAnalysisWorkload.MediumField, LanguageAnalysisWorkload.LongField)]
    public LanguageAnalysisWorkload Workload { get; set; }

    [Params(1, 2, 4)]
    public int IndexingProducers { get; set; }

    private LeanDocument[] _documents = [];
    private readonly List<string> _iterationPaths = [];
    private string[] _fields = [];

    /// <summary>Number of text fields analysed by one benchmark operation.</summary>
    public int AnalysedFieldCount => _documents.Sum(static document => document.Fields.Count);

    /// <summary>Number of emitted tokens in the fields analysed by one operation.</summary>
    public int AnalysedTokenCount { get; private set; }

    [GlobalSetup]
    public void Setup()
    {
        string[] fields = CreateFieldValues(Workload);
        _fields = fields;
        var analyser = AnalyserFactory.Create("en");
        var sink = new CountingTokenSink();
        foreach (string field in fields)
        {
            analyser.Analyse(field.AsSpan(), sink);
            AnalysedTokenCount += sink.Count;
            sink.Reset();
        }
        AnalysedTokenCount *= DocumentCount;

        _documents = new LeanDocument[DocumentCount];
        for (int documentIndex = 0; documentIndex < _documents.Length; documentIndex++)
        {
            var document = new LeanDocument();
            foreach (string field in fields)
                document.Add(new TextField("body", field));
            _documents[documentIndex] = document;
        }

        RunPreflight();
    }

    internal static int DocumentCountFor(LanguageAnalysisWorkload workload) => workload switch
    {
        LanguageAnalysisWorkload.ShortSingleField => 40_960,
        LanguageAnalysisWorkload.ManyShortFields => 10_240,
        LanguageAnalysisWorkload.MediumField => 10_240,
        LanguageAnalysisWorkload.LongField => 2_048,
        _ => throw new ArgumentOutOfRangeException(nameof(workload), workload, null)
    };

    private IndexWriterConfig CreateConfig(int producers) => new()
    {
        DefaultAnalyser = AnalyserFactory.Create("en"),
        IndexingConcurrency = producers,
        MaxBufferedDocs = DocumentCount * 2,
        RamBufferSizeMB = 256,
        DurableCommits = false
    };

    /// <summary>
    /// Untimed: indexes once with the configured producers and once with one producer,
    /// prints per-stage time and allocation, and requires identical persisted content.
    /// </summary>
    private void RunPreflight()
    {
        string current = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-language-preflight-{Guid.NewGuid():N}");
        string reference = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-language-reference-{Guid.NewGuid():N}");
        try
        {
            string stages = IndexWithStages(current, IndexingProducers);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"// Language indexing preflight: workload={Workload}, producers={IndexingProducers}, documents={DocumentCount}, {stages}"));

            IndexWithStages(reference, 1);
            string currentSignature = DescribeIndex(current);
            string referenceSignature = DescribeIndex(reference);
            if (!string.Equals(currentSignature, referenceSignature, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Persisted index with {IndexingProducers} producers differs from one producer.\n" +
                    $"producers={IndexingProducers}:\n{currentSignature}\nproducers=1:\n{referenceSignature}");
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"// Language indexing parity: workload={Workload}, producers={IndexingProducers} matches producers=1; {currentSignature.Replace('\n', ' ')}"));
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(current);
            BenchmarkHelpers.DeleteDirectory(reference);
        }
    }

    private string IndexWithStages(string path, int producers)
    {
        Directory.CreateDirectory(path);
        using var counters = AsyncIndexingDiagnostics.BeginMeasurement();
        AsyncIndexingDiagnostics.Reset();

        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        long started = Stopwatch.GetTimestamp();
        var directory = new MMapDirectory(path);
        var writer = new IndexWriter(directory, CreateConfig(producers));
        var (createMs, createBytes) = Stage(ref started, ref allocated);

        writer.AddDocumentsConcurrent(_documents);
        var (addMs, addBytes) = Stage(ref started, ref allocated);

        writer.Commit();
        var (commitMs, commitBytes) = Stage(ref started, ref allocated);
        int segments = writer.CommittedSegments.Count;

        writer.Dispose();
        directory.Dispose();
        var (disposeMs, disposeBytes) = Stage(ref started, ref allocated);

        var snapshot = AsyncIndexingDiagnostics.GetSnapshot();
        return string.Create(CultureInfo.InvariantCulture,
            $"segments={segments}, flushSnapshots={snapshot.FlushSnapshots}, flushSnapshotDocs={snapshot.FlushSnapshotDocuments}, " +
            $"createMs={createMs:F3}, createBytes={createBytes}, addMs={addMs:F3}, addBytes={addBytes}, " +
            $"commitMs={commitMs:F3}, commitBytes={commitBytes}, disposeMs={disposeMs:F3}, disposeBytes={disposeBytes}");
    }

    private static (double Milliseconds, long Bytes) Stage(ref long started, ref long allocated)
    {
        long now = Stopwatch.GetTimestamp();
        long bytes = GC.GetTotalAllocatedBytes(precise: true);
        var result = ((now - started) * 1000d / Stopwatch.Frequency, bytes - allocated);
        started = Stopwatch.GetTimestamp();
        allocated = GC.GetTotalAllocatedBytes(precise: true);
        return result;
    }

    /// <summary>
    /// Producer-order-independent description of the persisted index: document and token
    /// totals, per-term hit counts and score multisets (which depend on term frequency and
    /// field length) and a positional phrase over the first field's tokens.
    /// </summary>
    private string DescribeIndex(string path)
    {
        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        var lines = new List<string>
        {
            $"live={searcher.Stats.LiveDocCount}",
            $"bodyDocs={searcher.Stats.GetFieldDocCount("body")}",
            $"bodyLength={searcher.Stats.GetFieldLengthSum("body")}",
        };

        var tokens = new TermCollectingSink();
        var analyser = AnalyserFactory.Create("en");
        foreach (string field in _fields)
            analyser.Analyse(field.AsSpan(), tokens);

        foreach (string term in tokens.Terms.Order(StringComparer.Ordinal))
        {
            var hits = searcher.Search(new TermQuery("body", term), DocumentCount);
            var scores = hits.ScoreDocs.Select(static hit => hit.Score.ToString("R", CultureInfo.InvariantCulture))
                .GroupBy(static score => score, StringComparer.Ordinal)
                .OrderBy(static group => group.Key, StringComparer.Ordinal)
                .Select(static group => $"{group.Key}x{group.Count()}");
            lines.Add($"{term}:{hits.TotalHits}:{string.Join(',', scores)}");
        }

        var first = new TermCollectingSink();
        analyser.Analyse(_fields[0].AsSpan(), first);
        var phrase = new PhraseQuery("body", [.. first.Sequence.Select(static t => t.Term)], [.. first.Sequence.Select(static t => t.Position)]);
        lines.Add($"phrase:{searcher.Search(phrase, DocumentCount).TotalHits}");
        return string.Join('\n', lines);
    }

    private sealed class TermCollectingSink : ISpanTokenSink
    {
        private int _position = -1;

        public HashSet<string> Terms { get; } = new(StringComparer.Ordinal);

        public List<(string Term, int Position)> Sequence { get; } = [];

        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset, string type = Token.DefaultType,
            int positionIncrement = 1, byte[]? payload = null)
        {
            _position += positionIncrement;
            string term = text.ToString();
            Terms.Add(term);
            Sequence.Add((term, _position));
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        foreach (string path in _iterationPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _iterationPaths.Clear();
    }

    [Benchmark]
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public int IndexDocuments()
    {
        string path = Path.Combine(BenchmarkHelpers.TempRoot, $"lc-language-analysis-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _iterationPaths.Add(path);

        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, CreateConfig(IndexingProducers));

        writer.AddDocumentsConcurrent(_documents);
        writer.Commit();
        return AnalysedTokenCount;
    }

    private static string[] CreateFieldValues(LanguageAnalysisWorkload workload)
    {
        const string shortText = "The quick fox jumps over the lazy dog.";

        return workload switch
        {
            LanguageAnalysisWorkload.ShortSingleField => [shortText],
            LanguageAnalysisWorkload.ManyShortFields => Enumerable.Repeat(shortText, 12).ToArray(),
            LanguageAnalysisWorkload.MediumField => [string.Join(' ', Enumerable.Repeat(shortText, 16))],
            LanguageAnalysisWorkload.LongField => [string.Join(' ', Enumerable.Repeat(shortText, 128))],
            _ => throw new ArgumentOutOfRangeException(nameof(workload), workload, null)
        };
    }
}

public enum LanguageAnalysisWorkload
{
    ShortSingleField,
    ManyShortFields,
    MediumField,
    LongField
}
