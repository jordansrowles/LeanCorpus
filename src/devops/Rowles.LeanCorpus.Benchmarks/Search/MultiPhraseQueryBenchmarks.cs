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
/// Compares <see cref="MultiPhraseQuery"/> throughput against Lucene.NET <c>MultiPhraseQuery</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class MultiPhraseQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

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
    public int LeanCorpus_MultiPhraseQuery()
    {
        // Slot 0: "united" or "federal"; Slot 1: "states" or "government"
        var q = new Rowles.LeanCorpus.Search.Queries.MultiPhraseQuery(
            "body",
            [["united", "federal"], ["states", "government"]]);
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MultiPhraseQuery()
    {
        var q = new Lucene.Net.Search.MultiPhraseQuery();
        q.Add(new[] { new Term("body", "united"), new Term("body", "federal") });
        q.Add(new[] { new Term("body", "states"), new Term("body", "government") });
        return s_luceneSearcher!.Search(q, TopN).TotalHits;
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
