using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Scoring;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneDoubleField = Lucene.Net.Documents.DoubleField;
using LuceneRAMDirectory = Lucene.Net.Store.RAMDirectory;
using LuceneIndexWriter = Lucene.Net.Index.IndexWriter;
using LuceneIndexWriterConfig = Lucene.Net.Index.IndexWriterConfig;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures index-time sort overhead: sorted vs unsorted index creation.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
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
    public int LeanCorpus_Index_Unsorted()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-sort-{Guid.NewGuid():N}");
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
    public int LeanCorpus_Index_Sorted()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-sort-{Guid.NewGuid():N}");
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

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Index_Unsorted()
    {
        using var dir = new LuceneRAMDirectory();
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new LuceneIndexWriter(
            dir, new LuceneIndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        for (int i = 0; i < _documentsWithPrices.Length; i++)
        {
            var (body, price) = _documentsWithPrices[i];
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", body, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneDoubleField("price", price, Lucene.Net.Documents.Field.Store.NO));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return _documentsWithPrices.Length;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Index_Sorted()
    {
        using var dir = new LuceneRAMDirectory();
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new LuceneIndexWriter(
            dir, new LuceneIndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        // Pre-sort by price to simulate index-time sort (Lucene.Net 4.8 has no native index sort).
        var sorted = _documentsWithPrices.OrderBy(d => d.Price).ToArray();
        for (int i = 0; i < sorted.Length; i++)
        {
            var (body, price) = sorted[i];
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", body, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneDoubleField("price", price, Lucene.Net.Documents.Field.Store.NO));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return sorted.Length;
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
