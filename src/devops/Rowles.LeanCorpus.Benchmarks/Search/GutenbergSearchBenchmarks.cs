using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures search throughput on real Project Gutenberg ebook text.
/// Two LeanCorpus indexes are built once in setup (standard and English analysers),
/// plus a Lucene.NET index. The benchmark measures only the hot search path.
/// </summary>
/// <remarks>
/// Query terms are pre-processed through each analyser's pipeline so that the
/// query matches what is stored in the index (the English analyser stems index
/// tokens, so the query term must also be stemmed for a correct lookup).
/// </remarks>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class GutenbergSearchBenchmarks
{
    private const int TopN = 25;

    // Terms that appear across multiple books; root forms are preserved by Porter stemmer,
    // so results are directly comparable between analysers.
    [Params("love", "man", "night", "sea", "death")]
    public string SearchTerm { get; set; } = "love";

    // Per-param (query term) fields
    private string _standardQueryTerm = string.Empty;
    private string _englishQueryTerm  = string.Empty;
    private string _luceneQueryTerm   = string.Empty;

    // Indexes — built once regardless of SearchTerm [Params] combos
    private static readonly Lock s_gate = new();
    private static bool s_built;
    private static string s_standardIndexPath = string.Empty;
    private static string s_englishIndexPath  = string.Empty;
    private static string s_luceneIndexPath   = string.Empty;
    private static LeanIndexSearcher? s_standardSearcher;
    private static LeanIndexSearcher? s_englishSearcher;
    private static Lucene.Net.Store.FSDirectory? s_luceneDirectory;
    private static DirectoryReader? s_luceneReader;
    private static LuceneIndexSearcher? s_luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        _standardQueryTerm = AnalyseQueryTerm(new StandardAnalyser(), SearchTerm);
        _englishQueryTerm  = AnalyseQueryTerm(new EnglishAnalyser(),  SearchTerm);
        _luceneQueryTerm   = SearchTerm.ToLowerInvariant();

        EnsureIndexes();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Static index resources persist for class lifetime.
    }

    /// <summary>
    /// Searches the LeanCorpus standard-analyser index with a lowercased query term.
    /// </summary>
    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Standard_Search()
    {
        var results = s_standardSearcher!.Search(new LeanTermQuery("body", _standardQueryTerm), TopN);
        return results.TotalHits;
    }

    /// <summary>
    /// Searches the LeanCorpus English-analyser index with a Porter-stemmed query term.
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_English_Search()
    {
        var results = s_englishSearcher!.Search(new LeanTermQuery("body", _englishQueryTerm), TopN);
        return results.TotalHits;
    }

    /// <summary>
    /// Searches the Lucene.NET standard-analyser index for comparison.
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Search()
    {
        var query = new LuceneTermQuery(new Term("body", _luceneQueryTerm));
        var results = s_luceneSearcher!.Search(query, TopN);
        return results.TotalHits;
    }

    private static string AnalyseQueryTerm(IAnalyser analyser, string term)
    {
        var tokens = new List<Analysis.Token>();
        analyser.Analyse(term.AsSpan(), new CapturingTokenSink(tokens));
        return tokens.Count > 0 ? tokens[0].Text : term;
    }

    private sealed class CapturingTokenSink(List<Analysis.Token> tokens) : Analysis.ISpanTokenSink
    {
        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Analysis.Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
            => tokens.Add(new Analysis.Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
    }

    private static void EnsureIndexes()
    {
        if (s_built)
            return;

        lock (s_gate)
        {
            if (s_built)
                return;

            var paragraphs = GutenbergDataLoader.Load();
            s_standardIndexPath = BuildLeanIndex(paragraphs, new StandardAnalyser(), "standard");
            s_englishIndexPath  = BuildLeanIndex(paragraphs, new EnglishAnalyser(),  "english");
            s_luceneIndexPath   = BuildLuceneIndex(paragraphs);

            s_standardSearcher = new LeanIndexSearcher(new LeanMMapDirectory(s_standardIndexPath));
            s_englishSearcher  = new LeanIndexSearcher(new LeanMMapDirectory(s_englishIndexPath));

            s_luceneDirectory  = Lucene.Net.Store.FSDirectory.Open(new DirectoryInfo(s_luceneIndexPath));
            s_luceneReader     = DirectoryReader.Open(s_luceneDirectory);
            s_luceneSearcher   = new LuceneIndexSearcher(s_luceneReader);

            s_built = true;
        }
    }

    public static void CleanupLuceneResources()
    {
        if (!s_built)
            return;

        lock (s_gate)
        {
            if (!s_built)
                return;

            s_luceneSearcher = null;
            s_luceneReader?.Dispose();
            s_luceneReader = null;
            s_luceneDirectory?.Dispose();
            s_luceneDirectory = null;
            s_built = false;
        }
    }

    private static string BuildLeanIndex(BookParagraph[] paragraphs, IAnalyser analyser, string label)
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-realdata-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        var directory = new LeanMMapDirectory(path);
        using (var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig
        {
            DefaultAnalyser = analyser,
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            DurableCommits = false
        }))
        {
            foreach (var para in paragraphs)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",    para.Id));
                doc.Add(new LeanStringField("title", para.Title));
                doc.Add(new LeanTextField  ("body",  para.Body));
                writer.AddDocument(doc);
            }

            writer.Commit();
        }

        return path;
    }

    private static string BuildLuceneIndex(BookParagraph[] paragraphs)
    {
        var path = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-realdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        using var directory = Lucene.Net.Store.FSDirectory.Open(new DirectoryInfo(path));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using (var writer = new Lucene.Net.Index.IndexWriter(
            directory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser)))
        {
            foreach (var para in paragraphs)
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
        }

        return path;
    }


}
