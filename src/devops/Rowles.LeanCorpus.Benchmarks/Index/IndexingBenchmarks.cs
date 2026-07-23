using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanStringDocValues = Rowles.LeanCorpus.Document.Fields.StringDocValues;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class IndexingBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(IndexingWorkloadProfile.PostingsOnly, IndexingWorkloadProfile.StoredFields,
        IndexingWorkloadProfile.SortedDocValues)]
    public IndexingWorkloadProfile Profile { get; set; }

    private string[] _documents = [];

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_IndexDocuments()
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new MMapDirectory(path);
            using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                directory,
                new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig
                {
                    MaxBufferedDocs = 10_000,
                    RamBufferSizeMB = 256
                });

            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                AddLeanFields(doc, i, _documents[i]);
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
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            using var directory = new LuceneMMapDirectory(new DirectoryInfo(path));
            using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            using var writer = new Lucene.Net.Index.IndexWriter(
                directory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));

            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new Lucene.Net.Documents.Document();
                AddLuceneFields(doc, i, _documents[i]);
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

    private void AddLeanFields(LeanDocument document, int id, string body)
    {
        bool stored = Profile == IndexingWorkloadProfile.StoredFields;
        var docValues = Profile == IndexingWorkloadProfile.SortedDocValues
            ? LeanStringDocValues.Sorted
            : LeanStringDocValues.None;
        document.Add(new LeanStringField("id", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            stored, 1.0f, docValues));
        document.Add(new LeanTextField("body", body, stored));
    }

    private void AddLuceneFields(Lucene.Net.Documents.Document document, int id, string body)
    {
        var store = Profile == IndexingWorkloadProfile.StoredFields ? Field.Store.YES : Field.Store.NO;
        string identifier = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        document.Add(new LuceneStringField("id", identifier, store));
        document.Add(new LuceneTextField("body", body, store));
        if (Profile == IndexingWorkloadProfile.SortedDocValues)
            document.Add(new SortedDocValuesField("id", new BytesRef(identifier)));
    }

}

public enum IndexingWorkloadProfile
{
    PostingsOnly,
    StoredFields,
    SortedDocValues
}
