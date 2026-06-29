using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LeanSpanQuery = Rowles.LeanCorpus.Search.Queries.SpanQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="SpanNearQuery"/>, <see cref="SpanOrQuery"/>, and <see cref="SpanNotQuery"/>
/// throughput against Lucene.NET span queries.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class SpanQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("Near", "Or", "Not")]
    public string SpanType { get; set; } = "Near";

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
    public int LeanCorpus_SpanQuery()
    {
        var q = BuildLeanSpanQuery(SpanType);
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SpanQuery()
    {
        var q = BuildLuceneSpanQuery(SpanType);
        return s_luceneSearcher!.Search(q, TopN).TotalHits;
    }

    private static LeanSpanQuery BuildLeanSpanQuery(string type)
    {
        var t1 = new Rowles.LeanCorpus.Search.Queries.SpanTermQuery("body", "government");
        var t2 = new Rowles.LeanCorpus.Search.Queries.SpanTermQuery("body", "people");
        var t3 = new Rowles.LeanCorpus.Search.Queries.SpanTermQuery("body", "market");

        return type switch
        {
            "Near" => new Rowles.LeanCorpus.Search.Queries.SpanNearQuery([t1, t2], slop: 5, inOrder: true),
            "Or"   => new Rowles.LeanCorpus.Search.Queries.SpanOrQuery(t1, t2, t3),
            "Not"  => new Rowles.LeanCorpus.Search.Queries.SpanNotQuery(new Rowles.LeanCorpus.Search.Queries.SpanNearQuery([t1, t2], slop: 10), t3),
            _      => throw new InvalidOperationException($"Unknown span type '{type}'.")
        };
    }

    private static Lucene.Net.Search.Query BuildLuceneSpanQuery(string type)
    {
        var t1 = new Lucene.Net.Search.Spans.SpanTermQuery(new Term("body", "government"));
        var t2 = new Lucene.Net.Search.Spans.SpanTermQuery(new Term("body", "people"));
        var t3 = new Lucene.Net.Search.Spans.SpanTermQuery(new Term("body", "market"));

        return type switch
        {
            "Near" => new Lucene.Net.Search.Spans.SpanNearQuery([t1, t2], slop: 5, inOrder: true),
            "Or"   => new Lucene.Net.Search.Spans.SpanOrQuery(t1, t2, t3),
            "Not"  => new Lucene.Net.Search.Spans.SpanNotQuery(new Lucene.Net.Search.Spans.SpanNearQuery([t1, t2], 10, true), t3),
            _      => throw new InvalidOperationException($"Unknown span type '{type}'.")
        };
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
