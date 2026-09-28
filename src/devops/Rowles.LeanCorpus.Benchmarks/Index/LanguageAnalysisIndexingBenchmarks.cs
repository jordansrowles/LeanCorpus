using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures language-analysis allocations through the real DWPT indexing path
/// for short, multi-field, medium and long text workloads.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[KeepBenchmarkFiles]
[InvocationCount(1)]
public class LanguageAnalysisIndexingBenchmarks
{
    private const int DocumentCount = 128;

    [Params(LanguageAnalysisWorkload.ShortSingleField, LanguageAnalysisWorkload.ManyShortFields,
        LanguageAnalysisWorkload.MediumField, LanguageAnalysisWorkload.LongField)]
    public LanguageAnalysisWorkload Workload { get; set; }

    [Params(1, 2, 4)]
    public int IndexingProducers { get; set; }

    private LeanDocument[] _documents = [];
    private readonly List<string> _iterationPaths = [];

    /// <summary>Number of text fields analysed by one benchmark operation.</summary>
    public int AnalysedFieldCount => _documents.Sum(static document => document.Fields.Count);

    /// <summary>Number of emitted tokens in the fields analysed by one operation.</summary>
    public int AnalysedTokenCount { get; private set; }

    [GlobalSetup]
    public void Setup()
    {
        string[] fields = CreateFieldValues(Workload);
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
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DefaultAnalyser = AnalyserFactory.Create("en"),
            IndexingConcurrency = IndexingProducers,
            MaxBufferedDocs = DocumentCount * 2,
            RamBufferSizeMB = 256,
            DurableCommits = false
        });

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
