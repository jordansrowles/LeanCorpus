using BenchmarkDotNet.Attributes;
using Rowles.DataForge.Workloads.Legacy;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexWriter = Lucene.Net.Index.IndexWriter;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneDirectory = Lucene.Net.Store.Directory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures index build time on real Project Gutenberg ebook paragraphs.
/// Each iteration creates a fresh temporary index, indexes all paragraphs, commits, and cleans up.
/// Compares <see cref="StandardAnalyser"/> and <see cref="EnglishAnalyser"/> against Lucene.NET.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(2)]
[IterationCount(5)]
[InvocationCount(1)]
public class GutenbergIndexingBenchmarks
{
    public static IEnumerable<string> PersistenceProfiles => GutenbergPersistenceFixtureVerifier.GetProfileIds();

    [ParamsSource(nameof(PersistenceProfiles))]
    public string PersistenceProfile { get; set; } = "as-shipped-persistence";

    private BookParagraph[] _paragraphs = [];
    private readonly List<string> _iterationPaths = [];

    [GlobalSetup]
    public void Setup()
    {
        _ = GutenbergPersistenceFixtureVerifier.Verify(BenchmarkHelpers.RepositoryRoot);
        _paragraphs = GutenbergCorpusLoader.Load();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupIterationPaths();
        _paragraphs = [];
    }

    [IterationCleanup]
    public void CleanupIterationPaths()
    {
        foreach (var path in _iterationPaths)
            BenchmarkHelpers.DeleteDirectory(path);
        _iterationPaths.Clear();
    }

    /// <summary>
    /// Indexes all paragraphs with the standard analyser (tokenise+lowercase+stopwords).
    /// </summary>
    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Standard_Index() => RunLeanIndex(new StandardAnalyser());

    /// <summary>
    /// Indexes all paragraphs with the English analyser (tokenise+lowercase+stopwords+Porter stem).
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_English_Index() => RunLeanIndex(new EnglishAnalyser());

    /// <summary>
    /// Indexes all paragraphs with Lucene.NET's standard analyser for comparison.
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Index()
    {
        var profile = GutenbergPersistenceFixtureVerifier.GetProfile(PersistenceProfile);
        var settings = profile.Lucene;
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-realdata-idx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using LuceneDirectory directory = GutenbergPersistenceFixtureVerifier.OpenLuceneDirectory(settings.Directory, path);
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var config = new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
        {
            RAMBufferSizeMB = settings.RamBufferSizeMB,
            RAMPerThreadHardLimitMB = checked((int)settings.RamPerThreadHardLimitMB),
            MaxBufferedDocs = settings.MaxBufferedDocs,
            UseCompoundFile = settings.UseCompoundFile
        };
        using var writer = new LuceneIndexWriter(
            directory, config);

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

    private int RunLeanIndex(IAnalyser analyser)
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-realdata-idx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _iterationPaths.Add(path);
        using var directory = new MMapDirectory(path);
        var settings = GutenbergPersistenceFixtureVerifier.GetProfile(PersistenceProfile).LeanCorpus;
        using var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig
        {
            DefaultAnalyser = analyser,
            MaxBufferedDocs = settings.MaxBufferedDocs,
            RamBufferSizeMB = settings.RamBufferSizeMB,
            RamPerThreadHardLimitMB = settings.RamPerThreadHardLimitMB,
            DurableCommits = settings.DurableCommits,
            UseCompoundFile = settings.UseCompoundFile
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
}
