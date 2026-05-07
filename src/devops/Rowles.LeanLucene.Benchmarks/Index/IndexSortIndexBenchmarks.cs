using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Search.Scoring;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures index-time sort overhead: sorted vs unsorted index creation.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class IndexSortIndexBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private (string Body, double Price)[] _documentsWithPrices = [];

    [GlobalSetup]
    public void Setup()
    {
        _documentsWithPrices = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Index_Unsorted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new LeanMMapDirectory(path);
            using var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256
            });

            IndexDocuments(writer);
            writer.Commit();
            return _documentsWithPrices.Length;
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Index_Sorted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-sort-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new LeanMMapDirectory(path);
            using var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256,
                IndexSort = new IndexSort(SortField.Numeric("price"))
            });

            IndexDocuments(writer);
            writer.Commit();
            return _documentsWithPrices.Length;
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private void IndexDocuments(IndexWriter writer)
    {
        for (int i = 0; i < _documentsWithPrices.Length; i++)
        {
            var (body, price) = _documentsWithPrices[i];
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", body));
            doc.Add(new NumericField("price", price));
            writer.AddDocument(doc);
        }
    }
}
