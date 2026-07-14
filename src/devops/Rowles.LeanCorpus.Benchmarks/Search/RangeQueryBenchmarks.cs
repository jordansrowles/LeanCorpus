using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares BKD-backed <see cref="RangeQuery"/> throughput against Lucene.NET <c>NumericRangeQuery</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class RangeQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Range width as a fraction of the full 1–1000 price range.</summary>
    [Params(0.01, 0.1, 0.5)]
    public double RangeWidth { get; set; } = 0.1;

    // Index state — built once, shared across RangeWidth [Params] combos
    private static readonly System.Threading.Lock s_gate = new();
    private static int s_lastDocCount;
    private static bool s_built;
    private static string s_leanIndexPath = string.Empty;
    private static LeanMMapDirectory? s_leanDirectory;
    private static LeanIndexSearcher? s_leanSearcher;
    private static RAMDirectory? s_luceneDirectory;
    private static StandardAnalyzer? s_luceneAnalyzer;
    private static DirectoryReader? s_luceneReader;
    private static LuceneIndexSearcher? s_luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        EnsureIndexes();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Static resources persist for the lifetime of the benchmark class.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_RangeQuery()
    {
        double span = 1000.0 * RangeWidth;
        return s_leanSearcher!.Search(new RangeQuery("price", 100, 100 + span), TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NumericRangeQuery()
    {
        double span = 1000.0 * RangeWidth;
        var q = NumericRangeQuery.NewDoubleRange("price", 100.0, 100.0 + span, minInclusive: true, maxInclusive: true);
        return s_luceneSearcher!.Search(q, TopN).TotalHits;
    }

    private void EnsureIndexes()
    {
        if (s_built && s_lastDocCount == DocumentCount)
            return;

        lock (s_gate)
        {
            if (s_built && s_lastDocCount == DocumentCount)
                return;

            var docs = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);

            // LeanCorpus
            s_leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot,
                $"leancorpus-bench-range-{Guid.NewGuid():N}");
            IODirectory.CreateDirectory(s_leanIndexPath);
            s_leanDirectory = new LeanMMapDirectory(s_leanIndexPath);
            using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                s_leanDirectory,
                new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig
                { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 }))
            {
                for (int i = 0; i < docs.Length; i++)
                {
                    var doc = new LeanDocument();
                    doc.Add(new LeanStringField("id",
                        i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    doc.Add(new LeanTextField("body", docs[i].Body));
                    doc.Add(new LeanNumericField("price", docs[i].Price));
                    writer.AddDocument(doc);
                }
                writer.Commit();
            }
            s_leanSearcher = new LeanIndexSearcher(s_leanDirectory);

            // Lucene.NET
            s_luceneDirectory = new RAMDirectory();
            s_luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            using (var writer = new Lucene.Net.Index.IndexWriter(
                s_luceneDirectory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, s_luceneAnalyzer)))
            {
                for (int i = 0; i < docs.Length; i++)
                {
                    var doc = new Lucene.Net.Documents.Document
                    {
                        new StringField("id",
                            i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            Field.Store.NO),
                        new Lucene.Net.Documents.TextField("body", docs[i].Body,
                            Field.Store.NO),
                        new DoubleField("price", docs[i].Price, Field.Store.NO)
                    };
                    writer.AddDocument(doc);
                }
                writer.Commit();
            }
            s_luceneReader = DirectoryReader.Open(s_luceneDirectory);
            s_luceneSearcher = new LuceneIndexSearcher(s_luceneReader);

            s_lastDocCount = DocumentCount;
            s_built = true;
        }
    }

}
