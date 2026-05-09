using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Document.Json;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures schema validation overhead and JSON mapping throughput.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class SchemaAndJsonBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private string[] _jsonDocuments = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _jsonDocuments = BenchmarkData.BuildJsonDocuments(DocumentCount);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Index_NoSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new MMapDirectory(path);
            using var writer = new IndexWriter(directory, new IndexWriterConfig
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
    public int LeanLucene_Index_WithSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-schema-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var schema = new IndexSchema { StrictMode = true }
                .Add(new FieldMapping("id", FieldType.String))
                .Add(new FieldMapping("body", FieldType.Text));

            var directory = new MMapDirectory(path);
            using var writer = new IndexWriter(directory, new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256,
                Schema = schema
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
    public int LeanLucene_JsonMapping()
    {
        int fieldCount = 0;
        for (int i = 0; i < _jsonDocuments.Length; i++)
        {
            var doc = JsonDocumentMapper.FromJsonString(_jsonDocuments[i]);
            fieldCount += doc.Fields.Count;
        }
        return fieldCount;
    }
}
