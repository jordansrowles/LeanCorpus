using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures indexing overhead when token budget enforcement is enabled vs disabled.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[InvocationCount(1)]
public class TokenBudgetBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private readonly List<string> _createdPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        foreach (var path in _createdPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _createdPaths.Clear();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_NoBudget()
    {
        return IndexWithConfig(new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        });
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_WithBudget_Truncate()
    {
        return IndexWithConfig(new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            MaxTokensPerDocument = 100,
            TokenBudgetPolicy = TokenBudgetPolicy.Truncate
        });
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Index_WithBudget_Reject()
    {
        return IndexWithConfig(new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            MaxTokensPerDocument = 100,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject
        });
    }

    private int IndexWithConfig(IndexWriterConfig config)
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _createdPaths.Add(path);

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, config))
        {
            int accepted = 0;

            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));

                try
                {
                    writer.AddDocument(doc);
                    accepted++;
                }
                catch (TokenBudgetExceededException)
                {
                    // Expected in Reject mode — document is skipped
                }
            }

            writer.Commit();
            return accepted;
        }
    }
}
