using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Util;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanLucene.Benchmarks;

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class IndexingBenchmarks
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
    public int LeanLucene_IndexDocuments()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new MMapDirectory(path);
            using var writer = new Rowles.LeanLucene.Index.Indexer.IndexWriter(
                directory,
                new Rowles.LeanLucene.Index.Indexer.IndexWriterConfig
                {
                    MaxBufferedDocs = 10_000,
                    RamBufferSizeMB = 256
                });

            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                writer.AddDocument(doc);
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

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_IndexDocuments()
    {
        using var directory = new Lucene.Net.Store.RAMDirectory();
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            directory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));

        for (int i = 0; i < _documents.Length; i++)
        {
            var doc = new Lucene.Net.Documents.Document
            {
                new LuceneStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), Field.Store.NO),
                new LuceneTextField("body", _documents[i], Field.Store.NO)
            };
            writer.AddDocument(doc);
        }

        writer.Commit();
        return _documents.Length;
    }

}
