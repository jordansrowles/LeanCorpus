using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares BKD-backed <see cref="RangeQuery"/> throughput against Lucene.NET <c>NumericRangeQuery</c>.
/// Both engines use memory-mapped storage (ADR045). As-shipped rows use each writer's default
/// segment topology; single-segment rows force-merge both indexes to one segment.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class RangeQueryBenchmarks
{
    private const int TopN = 25;
    private const double RangeStart = 100.0;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Range width as a fraction of the full 1 to 1000 price range.</summary>
    [Params(0.01, 0.1, 0.5)]
    public double RangeWidth { get; set; } = 0.1;

    // Index state, built once and shared across RangeWidth [Params] combinations.
    private static readonly System.Threading.Lock s_gate = new();
    private static int s_lastDocCount;
    private static bool s_built;
    private static (string Body, double Price)[] s_documents = [];
    private static RangeIndexPair? s_asShipped;
    private static RangeIndexPair? s_singleSegment;

    [GlobalSetup]
    public void Setup()
    {
        EnsureIndexes();
        RunPreflight();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // Static resources persist for the lifetime of the benchmark class.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_RangeQuery()
        => s_asShipped!.LeanSearcher.Search(BuildLeanQuery(), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NumericRangeQuery()
        => s_asShipped!.LuceneSearcher.Search(BuildLuceneQuery(), TopN).TotalHits;

    [Benchmark(Description = "LeanCorpus range single segment")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_RangeQuery_SingleSegment()
        => s_singleSegment!.LeanSearcher.Search(BuildLeanQuery(), TopN).TotalHits;

    [Benchmark(Description = "Lucene.NET range single segment")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NumericRangeQuery_SingleSegment()
        => s_singleSegment!.LuceneSearcher.Search(BuildLuceneQuery(), TopN).TotalHits;

    private double RangeEnd => RangeStart + 1000.0 * RangeWidth;

    private RangeQuery BuildLeanQuery() => new("price", RangeStart, RangeEnd);

    private NumericRangeQuery<double> BuildLuceneQuery()
        => NumericRangeQuery.NewDoubleRange("price", RangeStart, RangeEnd, minInclusive: true, maxInclusive: true);

    /// <summary>
    /// Runs every row's query once outside timing. The expected count is derived from the
    /// generated prices, independently of either engine.
    /// </summary>
    private void RunPreflight()
    {
        double end = RangeEnd;
        int expected = 0;
        foreach (var (_, price) in s_documents)
        {
            if (price >= RangeStart && price <= end)
                expected++;
        }

        string caseName = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"RangeWidth={RangeWidth}");
        var observed = new (string Row, string Engine, int Hits)[]
        {
            ("as-shipped", BenchmarkFixtureEvidence.LeanCorpusEngine, LeanCorpus_RangeQuery()),
            ("as-shipped", BenchmarkFixtureEvidence.LuceneNetEngine, LuceneNet_NumericRangeQuery()),
            ("single-segment", BenchmarkFixtureEvidence.LeanCorpusEngine, LeanCorpus_RangeQuery_SingleSegment()),
            ("single-segment", BenchmarkFixtureEvidence.LuceneNetEngine, LuceneNet_NumericRangeQuery_SingleSegment())
        };
        foreach (var (row, engine, hits) in observed)
        {
            BenchmarkFixtureEvidence.RequireWork("range", string.Concat(caseName, " ", row), engine, hits);
            if (hits != expected)
                throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Range {caseName} {row} on {engine} returned {hits} hits; the generated prices give {expected}."));
        }
    }

    private void EnsureIndexes()
    {
        if (s_built && s_lastDocCount == DocumentCount)
            return;

        lock (s_gate)
        {
            if (s_built && s_lastDocCount == DocumentCount)
                return;

            s_documents = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
            s_asShipped = RangeIndexPair.Build(s_documents, singleSegment: false);
            s_singleSegment = RangeIndexPair.Build(s_documents, singleSegment: true);

            string count = DocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            s_asShipped.RecordTopology(string.Concat("range-as-shipped-", count));
            s_singleSegment.RecordTopology(string.Concat("range-single-segment-", count));
            s_lastDocCount = DocumentCount;
            s_built = true;
        }
    }

    private sealed class RangeIndexPair
    {
        private RangeIndexPair(
            LeanMMapDirectory leanDirectory,
            LeanIndexSearcher leanSearcher,
            LuceneMMapDirectory luceneDirectory,
            DirectoryReader luceneReader)
        {
            LeanDirectory = leanDirectory;
            LeanSearcher = leanSearcher;
            LuceneDirectory = luceneDirectory;
            LuceneReader = luceneReader;
            LuceneSearcher = new LuceneIndexSearcher(luceneReader);
        }

        public LeanMMapDirectory LeanDirectory { get; }

        public LeanIndexSearcher LeanSearcher { get; }

        public LuceneMMapDirectory LuceneDirectory { get; }

        public DirectoryReader LuceneReader { get; }

        public LuceneIndexSearcher LuceneSearcher { get; }

        public void RecordTopology(string fixture)
        {
            BenchmarkFixtureEvidence.RecordLeanCorpus(fixture, LeanDirectory, LeanSearcher);
            BenchmarkFixtureEvidence.RecordLucene(fixture, LuceneDirectory, LuceneReader);
        }

        public static RangeIndexPair Build((string Body, double Price)[] docs, bool singleSegment)
        {
            string leanPath = BenchmarkHelpers.CreateTempDirectory("leancorpus-bench-range");
            var leanDirectory = new LeanMMapDirectory(leanPath);
            using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                leanDirectory,
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
                if (singleSegment)
                {
                    writer.ForceMerge(1);
                    writer.Commit();
                }
            }
            var leanSearcher = new LeanIndexSearcher(leanDirectory);

            string lucenePath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-range");
            var luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(lucenePath));
            using (var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48))
            using (var writer = new Lucene.Net.Index.IndexWriter(
                luceneDirectory,
                new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser)))
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
                if (singleSegment)
                    writer.ForceMerge(1);
                writer.Commit();
            }
            var luceneReader = DirectoryReader.Open(luceneDirectory);

            var pair = new RangeIndexPair(leanDirectory, leanSearcher, luceneDirectory, luceneReader);
            if (singleSegment && (leanSearcher.GetSegmentReaders().Count != 1 || luceneReader.Leaves.Count != 1))
                throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Single-segment range fixture has {leanSearcher.GetSegmentReaders().Count} LeanCorpus and {luceneReader.Leaves.Count} Lucene.NET segments."));
            return pair;
        }
    }
}
