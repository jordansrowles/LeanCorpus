using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Util;
using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexWriter = Rowles.LeanLucene.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanLucene.Index.Indexer.IndexWriterConfig;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;
using LuceneIndexWriter = Lucene.Net.Index.IndexWriter;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures index build time on real Project Gutenberg ebook paragraphs.
/// Each iteration creates a fresh temporary index, indexes all paragraphs, commits, and cleans up.
/// Compares <see cref="StandardAnalyser"/> and <see cref="EnglishAnalyser"/> against Lucene.NET.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class GutenbergIndexingBenchmarks
{
    private BookParagraph[] _paragraphs = [];

    [GlobalSetup]
    public void Setup() => _paragraphs = GutenbergDataLoader.Load();

    [GlobalCleanup]
    public void Cleanup() => _paragraphs = [];

    /// <summary>
    /// Indexes all paragraphs with the standard analyser (tokenise+lowercase+stopwords).
    /// </summary>
    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Standard_Index() => RunLeanIndex(new StandardAnalyser());

    /// <summary>
    /// Indexes all paragraphs with the English analyser (tokenise+lowercase+stopwords+Porter stem).
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_English_Index() => RunLeanIndex(new EnglishAnalyser());

    /// <summary>
    /// Indexes all paragraphs with Lucene.NET's standard analyser for comparison.
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Index()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucenenet-realdata-idx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            using var directory = Lucene.Net.Store.FSDirectory.Open(new DirectoryInfo(path));
            using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            using var writer = new LuceneIndexWriter(
                directory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer));

            foreach (var para in _paragraphs)
            {
                var doc = new Lucene.Net.Documents.Document
                {
                    new LuceneStringField("id",    para.Id,    Field.Store.NO),
                    new LuceneStringField("title", para.Title, Field.Store.NO),
                    new LuceneTextField  ("body",  para.Body,  Field.Store.NO)
                };
                writer.AddDocument(doc);
            }

            writer.Commit();
            return _paragraphs.Length;
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }

    private int RunLeanIndex(IAnalyser analyser)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-realdata-idx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            var directory = new MMapDirectory(path);
            using var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig
            {
                DefaultAnalyser = analyser,
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256,
                DurableCommits = false
            });

            foreach (var para in _paragraphs)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",    para.Id));
                doc.Add(new LeanStringField("title", para.Title));
                doc.Add(new LeanTextField  ("body",  para.Body));
                writer.AddDocument(doc);
            }

            writer.Commit();
            return _paragraphs.Length;
        }
        finally
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
    }
}
