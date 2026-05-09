using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures indexing overhead when token budget enforcement is enabled vs disabled.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class TokenBudgetBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Index_NoBudget()
    {
        return IndexWithConfig(new IndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        });
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Index_WithBudget_Truncate()
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
    public int LeanLucene_Index_WithBudget_Reject()
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
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-budget-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new MMapDirectory(path);
            using var writer = new IndexWriter(directory, config);

            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));

                try
                {
                    writer.AddDocument(doc);
                }
                catch (TokenBudgetExceededException)
                {
                    // Expected in Reject mode — document is skipped
                }
            }

            writer.Commit();
            return _documents.Length;
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
