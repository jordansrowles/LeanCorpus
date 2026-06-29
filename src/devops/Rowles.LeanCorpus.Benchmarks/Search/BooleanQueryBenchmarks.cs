using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures BooleanQuery search performance across deterministic clause shapes.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class BooleanQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("Must2Common", "Must3Mixed", "Should2Common", "Should4Mixed", "MustNotCommon")]
    public string BooleanShape { get; set; } = "Must2Common";

    // Lucene.NET fields — built once per class regardless of [Params] combos
    private static readonly Lock s_luceneGate = new();
    private static bool s_luceneBuilt;
    private static LuceneMMapDirectory? s_luceneDirectory;
    private static StandardAnalyzer? s_luceneAnalyzer;
    private static DirectoryReader? s_luceneReader;
    private static LuceneIndexSearcher? s_luceneSearcher;

    private LeanIndexSearcher? _leanSearcher;
    private Rowles.LeanCorpus.Search.Query? _leanQuery;
    private Lucene.Net.Search.Query? _luceneQuery;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        EnsureLuceneIndex();
        _leanQuery = BuildLeanQuery(BooleanShape);
        _luceneQuery = BuildLuceneQuery(BooleanShape);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Lean resources are owned by SharedStandardIndex; do not dispose.
        // Lucene resources persist for the lifetime of this benchmark class.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BooleanQuery()
    {
        return _leanSearcher!.Search(_leanQuery!, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_BooleanQuery()
    {
        return s_luceneSearcher!.Search(_luceneQuery!, TopN).TotalHits;
    }

    // --- One-shot Lucene index builder ---

    private static void EnsureLuceneIndex()
    {
        if (s_luceneBuilt)
            return;

        lock (s_luceneGate)
        {
            if (s_luceneBuilt)
                return;

            var documents = SharedStandardIndex.Documents;
            var path = Path.Combine(BenchmarkHelpers.TempRoot,
                $"lucenenet-shared-stdidx-{Guid.NewGuid():N}");
            IODirectory.CreateDirectory(path);

            s_luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(path));
            s_luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

            using (var writer = new Lucene.Net.Index.IndexWriter(
                s_luceneDirectory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, s_luceneAnalyzer)))
            {
                for (int i = 0; i < documents.Length; i++)
                {
                    var doc = new Lucene.Net.Documents.Document
                    {
                        new LuceneStringField("id",
                            i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Lucene.Net.Documents.Field.Store.NO),
                        new LuceneTextField("body", documents[i],
                            Lucene.Net.Documents.Field.Store.NO)
                    };
                    writer.AddDocument(doc);
                }

                writer.Commit();
            }

            s_luceneReader = DirectoryReader.Open(s_luceneDirectory);
            s_luceneSearcher = new LuceneIndexSearcher(s_luceneReader);
            s_luceneBuilt = true;
        }
    }

    public static void CleanupLuceneResources()
    {
        if (!s_luceneBuilt)
            return;

        lock (s_luceneGate)
        {
            if (!s_luceneBuilt)
                return;

            s_luceneSearcher = null;
            s_luceneReader?.Dispose();
            s_luceneReader = null;
            s_luceneAnalyzer?.Dispose();
            s_luceneAnalyzer = null;
            s_luceneDirectory?.Dispose();
            s_luceneDirectory = null;
            s_luceneBuilt = false;
        }
    }

    private static Rowles.LeanCorpus.Search.Query BuildLeanQuery(string shape)
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        switch (shape)
        {
            case "Must2Common":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "said", Rowles.LeanCorpus.Search.Occur.Must);
                break;
            case "Must3Mixed":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "economic", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "people", Rowles.LeanCorpus.Search.Occur.Must);
                break;
            case "Should2Common":
                AddLean(builder, "market", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "people", Rowles.LeanCorpus.Search.Occur.Should);
                break;
            case "Should4Mixed":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "people", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "market", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "president", Rowles.LeanCorpus.Search.Occur.Should);
                break;
            case "MustNotCommon":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "market", Rowles.LeanCorpus.Search.Occur.MustNot);
                break;
            default:
                throw new InvalidOperationException($"Unknown boolean benchmark shape '{shape}'.");
        }

        return builder.Build();
    }

    private static Lucene.Net.Search.Query BuildLuceneQuery(string shape)
    {
        var query = new Lucene.Net.Search.BooleanQuery();
        switch (shape)
        {
            case "Must2Common":
                AddLucene(query, "government", Occur.MUST);
                AddLucene(query, "said", Occur.MUST);
                break;
            case "Must3Mixed":
                AddLucene(query, "government", Occur.MUST);
                AddLucene(query, "economic", Occur.MUST);
                AddLucene(query, "people", Occur.MUST);
                break;
            case "Should2Common":
                AddLucene(query, "market", Occur.SHOULD);
                AddLucene(query, "people", Occur.SHOULD);
                break;
            case "Should4Mixed":
                AddLucene(query, "government", Occur.SHOULD);
                AddLucene(query, "people", Occur.SHOULD);
                AddLucene(query, "market", Occur.SHOULD);
                AddLucene(query, "president", Occur.SHOULD);
                break;
            case "MustNotCommon":
                AddLucene(query, "government", Occur.MUST);
                AddLucene(query, "market", Occur.MUST_NOT);
                break;
            default:
                throw new InvalidOperationException($"Unknown boolean benchmark shape '{shape}'.");
        }

        return query;
    }

    private static void AddLean(
        Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder builder,
        string term,
        Rowles.LeanCorpus.Search.Occur occur)
    {
        builder.Add(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", term), occur);
    }

    private static void AddLucene(
        Lucene.Net.Search.BooleanQuery query,
        string term,
        Occur occur)
    {
        query.Add(new Lucene.Net.Search.TermQuery(new Term("body", term)), occur);
    }

}
