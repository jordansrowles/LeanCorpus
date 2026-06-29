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
/// Measures WildcardQuery performance: LeanCorpus vs Lucene.NET.
/// Lifti only supports prefix wildcard, so it is excluded.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class WildcardQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("gov*", "m*rket", "pre*dent")]
    public string WildcardPattern { get; set; } = "gov*";

    // Lucene.NET fields — built once per class regardless of [Params] combos
    private static readonly Lock s_luceneGate = new();
    private static bool s_luceneBuilt;
    private static LuceneMMapDirectory? s_luceneDirectory;
    private static StandardAnalyzer? s_luceneAnalyzer;
    private static DirectoryReader? s_luceneReader;
    private static LuceneIndexSearcher? s_luceneSearcher;

    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        EnsureLuceneIndex();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Lean resources owned by SharedStandardIndex; Lucene persists for class lifetime.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WildcardQuery()
    {
        var query = new Rowles.LeanCorpus.Search.Queries.WildcardQuery("body", WildcardPattern);
        return _leanSearcher!.Search(query, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_WildcardQuery()
    {
        var query = new Lucene.Net.Search.WildcardQuery(new Term("body", WildcardPattern));
        return s_luceneSearcher!.Search(query, TopN).TotalHits;
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
}
