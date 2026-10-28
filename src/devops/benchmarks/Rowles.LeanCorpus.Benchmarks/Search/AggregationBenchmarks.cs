using BenchmarkDotNet.Attributes;
using System.Text.Json;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Search.Aggregations;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanNumericField = Rowles.LeanCorpus.Document.Fields.NumericField;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneDoubleDocValuesField = Lucene.Net.Documents.DoubleDocValuesField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="NumericAggregator"/> throughput for Stats and Histogram aggregation types
/// via the <see cref="LeanIndexSearcher.SearchWithAggregations"/> convenience method.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class AggregationBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    // Lucene.NET index state
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;

    private static readonly AggregationRequest StatsRequest =
        new("price_stats", "price", AggregationType.Stats);

    private static readonly AggregationRequest HistogramRequest =
        new("price_hist", "price", AggregationType.Histogram) { HistogramInterval = 100.0 };

    private static readonly AggregationRequest CardinalityRequest =
        new("price_cardinality", "price", AggregationType.Cardinality);

    private static readonly AggregationRequest TDigestRequest =
        new("price_percentiles", "price", AggregationType.TDigestPercentiles) { Percentiles = [50, 95, 99] };

    private static readonly AggregationRequest HdrRequest =
        new("latency_percentiles", "latency", AggregationType.HdrPercentiles)
        {
            HdrHighestTrackableValue = 1_000_000,
            Percentiles = [50, 95, 99]
        };

    [GlobalSetup]
    public void Setup()
    {
        var docs = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        try
        {
            BuildLeanIndex(docs);
            BuildLuceneIndex(docs);
            ValidateBval02NumericDocValuesFixture();
        }
        catch
        {
            _leanSearcher?.Dispose();
            _luceneReader?.Dispose();
            _luceneDirectory?.Dispose();
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
            throw;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchOnly()
        => _leanSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithStats()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, StatsRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithHistogram()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, HistogramRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithStatsAndHistogram()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, StatsRequest, HistogramRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithCardinality()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, CardinalityRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithTDigestPercentiles()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, TDigestRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SearchWithHdrPercentiles()
    {
        var (results, _) = _leanSearcher!.SearchWithAggregations(
            new TermQuery("body", "government"), TopN, HdrRequest);
        return results.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
    {
        var q = new LuceneTermQuery(new LuceneTerm("body", "government"));
        return _luceneSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public double LuceneNet_SearchWithStats()
    {
        LuceneNumericStats stats = ComputeLuceneStats(_luceneSearcher!, _luceneReader!);
        return stats.Count + stats.Sum + stats.SumOfSquares + stats.Minimum
            + stats.Maximum + stats.Mean + stats.PopulationVariance;
    }

    private void ValidateBval02NumericDocValuesFixture()
    {
        const string fixtureRelativePath = ".config/devops/benchmark-correctness-fixtures/bval-02-numeric-docvalues-multileaf.json";
        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            fixtureRelativePath.Replace('/', Path.DirectorySeparatorChar));
        using var document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(),
                "bval-02-numeric-docvalues-multileaf", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidOperationException($"BVAL-02 fixture metadata is invalid: {fixtureRelativePath}.");

        JsonElement input = root.GetProperty("input");
        if (!string.Equals(input.GetProperty("numericEncoding").GetString(),
                "IEEE754-binary64-raw-bits", StringComparison.Ordinal))
            throw new InvalidOperationException($"BVAL-02 fixture numeric encoding is unsupported: {fixtureRelativePath}.");

        var segmentElements = input.GetProperty("segments").EnumerateArray().ToArray();
        var segments = new List<NumericFixtureSegment>(segmentElements.Length);
        foreach (JsonElement segmentElement in segmentElements)
        {
            var fixtureDocuments = new List<NumericFixtureDocument>();
            foreach (JsonElement documentElement in segmentElement.GetProperty("documents").EnumerateArray())
            {
                if (!documentElement.GetProperty("numericDocValuePresent").GetBoolean())
                    throw new InvalidOperationException("BVAL-02 fixture requires a numeric DocValue for every document.");
                fixtureDocuments.Add(new NumericFixtureDocument(
                    documentElement.GetProperty("id").GetString()!,
                    documentElement.GetProperty("price").GetDouble()));
            }
            segments.Add(new NumericFixtureSegment(
                segmentElement.GetProperty("id").GetString()!, [.. fixtureDocuments]));
        }

        if (segments.Count != 2 || segments.Any(static segment => segment.Documents.Length == 0)
            || segments.SelectMany(static segment => segment.Documents).Select(static item => item.Id)
                .Distinct(StringComparer.Ordinal).Count() != segments.Sum(static segment => segment.Documents.Length))
            throw new InvalidOperationException("BVAL-02 fixture must define two non-empty segments with unique document IDs.");

        JsonElement expected = root.GetProperty("expected");
        var expectedStats = new LuceneNumericStats(
            expected.GetProperty("count").GetInt32(),
            expected.GetProperty("sum").GetDouble(),
            expected.GetProperty("sumOfSquares").GetDouble(),
            expected.GetProperty("minimum").GetDouble(),
            expected.GetProperty("maximum").GetDouble(),
            expected.GetProperty("mean").GetDouble(),
            expected.GetProperty("populationVariance").GetDouble());
        if (!expected.GetProperty("allValuesReadFromEverySegment").GetBoolean()
            || expectedStats.Count != segments.Sum(static segment => segment.Documents.Length))
            throw new InvalidOperationException("BVAL-02 fixture expected statistics do not cover every segment document.");

        string indexPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-02-numeric-docvalues");
        try
        {
            using var directory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(indexPath));
            using var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var writerConfig = new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser)
            {
                MergePolicy = Lucene.Net.Index.NoMergePolicy.NO_COMPOUND_FILES,
            };
            using (var writer = new Lucene.Net.Index.IndexWriter(directory, writerConfig))
            {
                foreach (NumericFixtureSegment segment in segments)
                {
                    foreach (NumericFixtureDocument fixtureDocument in segment.Documents)
                    {
                        var luceneDocument = new LuceneDocument();
                        luceneDocument.Add(new LuceneStringField("id", fixtureDocument.Id,
                            Lucene.Net.Documents.Field.Store.NO));
                        luceneDocument.Add(new LuceneTextField("body", "government",
                            Lucene.Net.Documents.Field.Store.NO));
                        luceneDocument.Add(new LuceneDoubleDocValuesField("price", fixtureDocument.Price));
                        writer.AddDocument(luceneDocument);
                    }
                    writer.Commit();
                }
            }

            using var reader = LuceneDirectoryReader.Open(directory);
            if (reader.Leaves.Count != segments.Count)
                throw new InvalidOperationException(
                    $"BVAL-02 fixture expected {segments.Count} Lucene segments but created {reader.Leaves.Count}.");

            var searcher = new LuceneIndexSearcher(reader);
            LuceneNumericStats actualStats = ComputeLuceneStats(searcher, reader);
            AssertFixtureStats(actualStats, expectedStats, fixtureRelativePath);
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(indexPath);
        }
    }

    private static void AssertFixtureStats(LuceneNumericStats actual, LuceneNumericStats expected,
        string fixturePath)
    {
        static bool NearlyEqual(double left, double right)
            => Math.Abs(left - right) <= 1e-9 * Math.Max(1d, Math.Abs(right));

        if (actual.Count != expected.Count
            || !NearlyEqual(actual.Sum, expected.Sum)
            || !NearlyEqual(actual.SumOfSquares, expected.SumOfSquares)
            || !NearlyEqual(actual.Minimum, expected.Minimum)
            || !NearlyEqual(actual.Maximum, expected.Maximum)
            || !NearlyEqual(actual.Mean, expected.Mean)
            || !NearlyEqual(actual.PopulationVariance, expected.PopulationVariance))
            throw new InvalidOperationException(
                $"BVAL-02 statistics differ for {fixturePath}: expected {expected}, actual {actual}.");
    }

    private static LuceneNumericStats ComputeLuceneStats(LuceneIndexSearcher searcher,
        LuceneDirectoryReader reader)
    {
        var query = new LuceneTermQuery(new LuceneTerm("body", "government"));
        var hits = searcher.Search(query, reader.MaxDoc);
        if (hits.TotalHits != hits.ScoreDocs.Length)
            throw new InvalidOperationException(
                $"BVAL-02 numeric aggregation expected every match, but TopDocs retained {hits.ScoreDocs.Length} of {hits.TotalHits} hits.");

        var leaves = reader.Leaves;
        if (leaves.Count == 0)
            throw new InvalidOperationException("BVAL-02 numeric aggregation found no Lucene segments.");

        var valuesByLeaf = new NumericDocValues[leaves.Count];
        var presenceByLeaf = new Lucene.Net.Util.IBits[leaves.Count];
        for (int leafIndex = 0; leafIndex < leaves.Count; leafIndex++)
        {
            var atomicReader = leaves[leafIndex].AtomicReader;
            valuesByLeaf[leafIndex] = atomicReader.GetNumericDocValues("price")
                ?? throw new InvalidOperationException(
                    $"BVAL-02 numeric DocValues are missing from Lucene segment {leafIndex}.");
            presenceByLeaf[leafIndex] = atomicReader.GetDocsWithField("price")
                ?? throw new InvalidOperationException(
                    $"BVAL-02 price DocValues are absent from Lucene segment {leafIndex}.");
        }

        double sum = 0d;
        double sumOfSquares = 0d;
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        int count = 0;
        foreach (var hit in hits.ScoreDocs)
        {
            int leafIndex = ReaderUtil.SubIndex(hit.Doc, leaves);
            var leaf = leaves[leafIndex];
            int localDocumentId = hit.Doc - leaf.DocBase;
            if (!presenceByLeaf[leafIndex].Get(localDocumentId))
                throw new InvalidOperationException(
                    $"BVAL-02 price DocValue is missing for Lucene document {hit.Doc} in segment {leafIndex}.");

            double price = BitConverter.Int64BitsToDouble(valuesByLeaf[leafIndex].Get(localDocumentId));
            sum += price;
            sumOfSquares += price * price;
            minimum = Math.Min(minimum, price);
            maximum = Math.Max(maximum, price);
            count++;
        }

        if (count == 0)
            throw new InvalidOperationException("BVAL-02 numeric aggregation returned no matching documents.");

        double mean = sum / count;
        double populationVariance = sumOfSquares / count - mean * mean;
        return new LuceneNumericStats(count, sum, sumOfSquares, minimum, maximum, mean, populationVariance);
    }

    private void BuildLuceneIndex((string Body, double Price)[] docs)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-agg-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);
        _luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id",
                i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField("body", docs[i].Body,
                Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneDoubleDocValuesField("price", docs[i].Price));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

    private void BuildLeanIndex((string Body, double Price)[] docs)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-agg-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        for (int i = 0; i < docs.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", docs[i].Body));
            doc.Add(new LeanNumericField("price", docs[i].Price));
            doc.Add(new Rowles.LeanCorpus.Document.Fields.Int64Field("latency", Math.Max(0, (long)Math.Round(docs[i].Price * 100))));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private readonly record struct LuceneNumericStats(int Count, double Sum, double SumOfSquares,
        double Minimum, double Maximum, double Mean, double PopulationVariance);

    private sealed record NumericFixtureSegment(string Id, NumericFixtureDocument[] Documents);

    private readonly record struct NumericFixtureDocument(string Id, double Price);
}
