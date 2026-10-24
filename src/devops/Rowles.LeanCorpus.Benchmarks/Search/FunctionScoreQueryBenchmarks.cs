using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Queries;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Search.Scoring;
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
using LuceneDoubleField = Lucene.Net.Documents.DoubleField;
using LuceneDoubleDocValuesField = Lucene.Net.Documents.DoubleDocValuesField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;

// Disambiguate TermQuery — the global using brings in Rowles.LeanCorpus.Search.Queries.TermQuery
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="FunctionScoreQuery"/> latency across all <see cref="ScoreMode"/> values.
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class FunctionScoreQueryBenchmarks
{
    private const int TopN = 25;
    private const string Bval04FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-04-function-score-modes-docvalues.json";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("Multiply", "Replace", "Sum", "Max")]
    public string Mode { get; set; } = "Multiply";

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    // Lucene.NET index state
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;

    private string _bval04LeanIndexPath = string.Empty;
    private LeanMMapDirectory? _bval04LeanDirectory;
    private LeanIndexSearcher? _bval04LeanSearcher;
    private string _bval04LuceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _bval04LuceneDirectory;
    private StandardAnalyzer? _bval04LuceneAnalyzer;
    private LuceneDirectoryReader? _bval04LuceneReader;
    private LuceneIndexSearcher? _bval04LuceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var docs = BenchmarkData.BuildDocumentsWithPrices(DocumentCount);
        BuildLeanIndex(docs);
        BuildLuceneIndex(docs);
        ValidateBval04Fixture();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        _bval04LeanSearcher?.Dispose();
        _bval04LeanDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_bval04LeanIndexPath) && IODirectory.Exists(_bval04LeanIndexPath))
            IODirectory.Delete(_bval04LeanIndexPath, recursive: true);
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _bval04LuceneReader?.Dispose();
        _bval04LuceneAnalyzer?.Dispose();
        _bval04LuceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_bval04LuceneIndexPath) && IODirectory.Exists(_bval04LuceneIndexPath))
            IODirectory.Delete(_bval04LuceneIndexPath, recursive: true);
        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    [Benchmark(Description = "LeanCorpus base term query")]
    [BenchmarkCategory("FunctionScore base-query reference")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BaseTermQuery()
        => _leanSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark(Description = "LeanCorpus function-score query")]
    [BenchmarkCategory("FunctionScore mode workload")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_FunctionScoreQuery()
    {
        var mode = Mode switch
        {
            "Multiply" => ScoreMode.Multiply,
            "Replace"  => ScoreMode.Replace,
            "Sum"      => ScoreMode.Sum,
            "Max"      => ScoreMode.Max,
            _          => ScoreMode.Multiply
        };
        var q = new FunctionScoreQuery(new TermQuery("body", "government"), "price", mode);
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark(Description = "Lucene.NET base term query")]
    [BenchmarkCategory("FunctionScore base-query reference")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
    {
        var q = new LuceneTermQuery(new LuceneTerm("body", "government"));
        return _luceneSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark(Description = "Lucene.NET function-score query")]
    [BenchmarkCategory("FunctionScore mode workload")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_FunctionScoreQuery()
    {
        var inner = new LuceneTermQuery(new LuceneTerm("body", "government"));
        var csq = new PriceBoostingQuery(inner, ParseMode(Mode));
        return _luceneSearcher!.Search(csq, TopN).TotalHits;
    }

    private static ScoreMode ParseMode(string mode)
        => mode switch
        {
            "Multiply" => ScoreMode.Multiply,
            "Replace" => ScoreMode.Replace,
            "Sum" => ScoreMode.Sum,
            "Max" => ScoreMode.Max,
            _ => throw new InvalidOperationException($"Unknown function-score mode '{mode}'.")
        };

    /// <summary>CustomScoreQuery that maps price DocValues through the selected score mode.</summary>
    private sealed class PriceBoostingQuery : CustomScoreQuery
    {
        private readonly ScoreMode _mode;

        public PriceBoostingQuery(Query subQuery, ScoreMode mode) : base(subQuery)
            => _mode = mode;

        protected override CustomScoreProvider GetCustomScoreProvider(AtomicReaderContext context)
            => new PriceProvider(context, _mode);

        private sealed class PriceProvider : CustomScoreProvider
        {
            private readonly NumericDocValues? _priceValues;
            private readonly Lucene.Net.Util.IBits? _pricePresence;
            private readonly ScoreMode _mode;

            public PriceProvider(AtomicReaderContext context, ScoreMode mode) : base(context)
            {
                var reader = (AtomicReader)context.Reader;
                _priceValues = reader.GetNumericDocValues("price");
                _pricePresence = reader.GetDocsWithField("price");
                _mode = mode;
            }

            public override float CustomScore(int doc, float subQueryScore, float valSrcScore)
            {
                if (_priceValues is null || _pricePresence is null || !_pricePresence.Get(doc))
                    return subQueryScore;

                double price = BitConverter.Int64BitsToDouble(_priceValues.Get(doc));
                return FunctionScoreQuery.Combine(subQueryScore, price, _mode);
            }
        }
    }

    private void ValidateBval04Fixture()
    {
        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            Bval04FixturePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(),
                "bval-04-function-score-modes-docvalues", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidOperationException($"BVAL-04 fixture metadata is invalid: {Bval04FixturePath}.");

        JsonElement input = root.GetProperty("input");
        if (!string.Equals(input.GetProperty("numericEncoding").GetString(),
                "IEEE754-binary64-raw-bits", StringComparison.Ordinal))
            throw new InvalidOperationException("BVAL-04 fixture numeric DocValues encoding is unsupported.");

        FunctionScoreFixtureSegment[] segments = input.GetProperty("segments").EnumerateArray()
            .Select(static segment => new FunctionScoreFixtureSegment(
                segment.GetProperty("id").GetString()!,
                segment.GetProperty("documents").EnumerateArray().Select(static item =>
                {
                    bool present = item.GetProperty("priceDocValuePresent").GetBoolean();
                    JsonElement priceElement = item.GetProperty("price");
                    double? price = priceElement.ValueKind == JsonValueKind.Null ? null : priceElement.GetDouble();
                    if (present != price.HasValue)
                        throw new InvalidOperationException("BVAL-04 fixture price value and DocValues-presence flag disagree.");
                    return new FunctionScoreFixtureDocument(item.GetProperty("id").GetString()!, price, present);
                }).ToArray()))
            .ToArray();
        FunctionScoreFixtureDocument[] fixtureDocuments = segments
            .SelectMany(static segment => segment.Documents).ToArray();
        string[] modes = ReadJsonStrings(input.GetProperty("modes"));
        double baseScore = input.GetProperty("baseScore").GetDouble();
        JsonElement expected = root.GetProperty("expected");
        int expectedSegmentCount = expected.GetProperty("expectedSegmentCount").GetInt32();
        if (segments.Length != expectedSegmentCount
            || segments.Any(static segment => segment.Documents.Length == 0)
            || fixtureDocuments.Length < 2
            || fixtureDocuments.Select(static item => item.Id).Distinct(StringComparer.Ordinal).Count() != fixtureDocuments.Length
            || fixtureDocuments.All(static item => item.PriceDocValuePresent)
            || fixtureDocuments.All(static item => !item.PriceDocValuePresent)
            || !fixtureDocuments.Any(static item => item.Price is < 0d)
            || !expected.GetProperty("allPresentPriceDocValuesAreRead").GetBoolean()
            || !expected.GetProperty("missingPriceUsesBaseScore").GetBoolean()
            || !expected.GetProperty("nonPositivePriceUsesSelectedMode").GetBoolean()
            || !expected.GetProperty("logicalTopKMustMatchForEachMode").GetBoolean()
            || !modes.SequenceEqual(["Multiply", "Replace", "Sum", "Max"], StringComparer.Ordinal)
            || !double.IsFinite(baseScore) || baseScore <= 0d)
            throw new InvalidOperationException("BVAL-04 fixture does not define the required multi-leaf mode, missing-value and non-positive-value cases.");

        _bval04LeanIndexPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-04-function-score-lean");
        _bval04LuceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-04-function-score-lucene");
        _bval04LeanDirectory = new LeanMMapDirectory(_bval04LeanIndexPath);
        using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _bval04LeanDirectory, new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig()))
        {
            foreach (FunctionScoreFixtureSegment segment in segments)
            {
                foreach (FunctionScoreFixtureDocument fixtureDocument in segment.Documents)
                {
                    var leanDocument = new LeanDocument();
                    leanDocument.Add(new LeanStringField("id", fixtureDocument.Id));
                    leanDocument.Add(new LeanTextField("body", "government"));
                    if (fixtureDocument.PriceDocValuePresent)
                        leanDocument.Add(new LeanNumericField("price", fixtureDocument.Price!.Value));
                    writer.AddDocument(leanDocument);
                }
                writer.Commit();
            }
        }
        _bval04LeanSearcher = new LeanIndexSearcher(_bval04LeanDirectory);
        using (var inspector = new Rowles.LeanCorpus.Index.Inspection.IndexInspector(_bval04LeanIndexPath))
        {
            int leanSegmentCount = inspector.Overview.SegmentCount;
            if (leanSegmentCount != expectedSegmentCount)
                throw new InvalidOperationException(
                    $"BVAL-04 fixture expected {expectedSegmentCount} LeanCorpus segments but created {leanSegmentCount}.");
        }

        _bval04LuceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_bval04LuceneIndexPath));
        _bval04LuceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var luceneWriterConfig = new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _bval04LuceneAnalyzer)
        {
            MergePolicy = Lucene.Net.Index.NoMergePolicy.NO_COMPOUND_FILES
        };
        using (var writer = new Lucene.Net.Index.IndexWriter(_bval04LuceneDirectory, luceneWriterConfig))
        {
            foreach (FunctionScoreFixtureSegment segment in segments)
            {
                foreach (FunctionScoreFixtureDocument fixtureDocument in segment.Documents)
                {
                    var luceneDocument = new LuceneDocument
                    {
                        new LuceneStringField("id", fixtureDocument.Id, Lucene.Net.Documents.Field.Store.NO),
                        new LuceneTextField("body", "government", Lucene.Net.Documents.Field.Store.NO)
                    };
                    if (fixtureDocument.PriceDocValuePresent)
                    {
                        luceneDocument.Add(new LuceneDoubleField("price", fixtureDocument.Price!.Value,
                            Lucene.Net.Documents.Field.Store.NO));
                        luceneDocument.Add(new LuceneDoubleDocValuesField("price", fixtureDocument.Price.Value));
                    }
                    writer.AddDocument(luceneDocument);
                }
                writer.Commit();
            }
        }
        _bval04LuceneReader = LuceneDirectoryReader.Open(_bval04LuceneDirectory);
        if (_bval04LuceneReader.Leaves.Count != expectedSegmentCount)
            throw new InvalidOperationException(
                $"BVAL-04 fixture expected {expectedSegmentCount} Lucene segments but created {_bval04LuceneReader.Leaves.Count}.");
        _bval04LuceneSearcher = new LuceneIndexSearcher(_bval04LuceneReader);
        ValidateLucenePriceDocValues(_bval04LuceneReader, fixtureDocuments, expected);

        string[] logicalIds = fixtureDocuments.Select(static item => item.Id).ToArray();
        Rowles.LeanCorpus.Search.Query leanBaseQuery = new Rowles.LeanCorpus.Search.Queries.ConstantScoreQuery(
            new TermQuery("body", "government"), (float)baseScore);
        Lucene.Net.Search.Query luceneBaseQuery = new Lucene.Net.Search.ConstantScoreQuery(
            new LuceneTermQuery(new LuceneTerm("body", "government")));
        var leanBaseScores = ReadLeanScores(
            _bval04LeanSearcher.Search(leanBaseQuery, logicalIds.Length), logicalIds, out _);
        var luceneBaseScores = ReadLuceneScores(
            _bval04LuceneSearcher.Search(luceneBaseQuery, logicalIds.Length), logicalIds, out _);
        AssertFixtureBaseScores(leanBaseScores, logicalIds, baseScore, "LeanCorpus");
        AssertFixtureBaseScores(luceneBaseScores, logicalIds, baseScore, "Lucene.NET");

        foreach (string modeName in modes)
        {
            ScoreMode mode = ParseMode(modeName);
            Dictionary<string, double> expectedScores = ReadExpectedScores(expected, modeName);
            string[] expectedTopK = ReadJsonStrings(expected.GetProperty("logicalTopKByMode").GetProperty(modeName));
            var expectedFormulaScores = fixtureDocuments.ToDictionary(
                static item => item.Id,
                item => item.PriceDocValuePresent
                    ? CalculateExpectedScore(baseScore, item.Price!.Value, modeName)
                    : baseScore,
                StringComparer.Ordinal);
            foreach (FunctionScoreFixtureDocument fixtureDocument in fixtureDocuments)
            {
                if (!expectedScores.TryGetValue(fixtureDocument.Id, out double fixtureScore)
                    || !NearlyEqual(fixtureScore, expectedFormulaScores[fixtureDocument.Id]))
                    throw new InvalidOperationException(
                        $"BVAL-04 fixture score formula is inconsistent for {modeName}/{fixtureDocument.Id}.");
            }

            Rowles.LeanCorpus.Search.Query leanFunctionQuery = new FunctionScoreQuery(leanBaseQuery,
                "price", mode);
            Lucene.Net.Search.Query luceneFunctionQuery = new PriceBoostingQuery(luceneBaseQuery, mode);
            var actualLeanScores = ReadLeanScores(
                _bval04LeanSearcher.Search(leanFunctionQuery, logicalIds.Length),
                logicalIds, out string[] actualLeanTopK);
            var actualLuceneScores = ReadLuceneScores(
                _bval04LuceneSearcher.Search(luceneFunctionQuery, logicalIds.Length),
                logicalIds, out string[] actualLuceneTopK);
            AssertFixtureScores(expectedScores, actualLeanScores, modeName, "LeanCorpus");
            AssertFixtureScores(expectedScores, actualLuceneScores, modeName, "Lucene.NET");
            AssertFixtureTopK(expectedTopK, actualLeanTopK, modeName, "LeanCorpus");
            AssertFixtureTopK(expectedTopK, actualLuceneTopK, modeName, "Lucene.NET");
        }
    }

    private static void ValidateLucenePriceDocValues(
        LuceneDirectoryReader reader,
        FunctionScoreFixtureDocument[] fixtureDocuments,
        JsonElement expected)
    {
        var valuesByLeaf = new NumericDocValues[reader.Leaves.Count];
        var presenceByLeaf = new Lucene.Net.Util.IBits[reader.Leaves.Count];
        for (int leafIndex = 0; leafIndex < reader.Leaves.Count; leafIndex++)
        {
            AtomicReader atomicReader = reader.Leaves[leafIndex].AtomicReader;
            valuesByLeaf[leafIndex] = atomicReader.GetNumericDocValues("price")
                ?? throw new InvalidOperationException($"BVAL-04 price NumericDocValues missing from leaf {leafIndex}.");
            presenceByLeaf[leafIndex] = atomicReader.GetDocsWithField("price")
                ?? throw new InvalidOperationException($"BVAL-04 price DocValues presence missing from leaf {leafIndex}.");
        }

        for (int globalDocId = 0; globalDocId < fixtureDocuments.Length; globalDocId++)
        {
            int leafIndex = ReaderUtil.SubIndex(globalDocId, reader.Leaves);
            int localDocId = globalDocId - reader.Leaves[leafIndex].DocBase;
            FunctionScoreFixtureDocument fixtureDocument = fixtureDocuments[globalDocId];
            bool hasValue = presenceByLeaf[leafIndex].Get(localDocId);
            if (hasValue != fixtureDocument.PriceDocValuePresent)
                throw new InvalidOperationException(
                    $"BVAL-04 price DocValues presence differs for {fixtureDocument.Id}.");
            if (hasValue)
            {
                double actualPrice = BitConverter.Int64BitsToDouble(valuesByLeaf[leafIndex].Get(localDocId));
                if (!NearlyEqual(actualPrice, fixtureDocument.Price!.Value))
                    throw new InvalidOperationException(
                        $"BVAL-04 price DocValues differ for {fixtureDocument.Id}: expected {fixtureDocument.Price}, actual {actualPrice}.");
            }
        }

        if (!expected.GetProperty("allPresentPriceDocValuesAreRead").GetBoolean())
            throw new InvalidOperationException("BVAL-04 fixture does not require reading every present price DocValue.");
    }

    private static Dictionary<string, double> ReadExpectedScores(JsonElement expected, string mode)
        => expected.GetProperty("scoresByMode").GetProperty(mode).EnumerateObject()
            .ToDictionary(static item => item.Name, static item => item.Value.GetDouble(), StringComparer.Ordinal);

    private static double CalculateExpectedScore(double baseScore, double price, string mode)
        => mode switch
        {
            "Multiply" => baseScore * price,
            "Replace" => price,
            "Sum" => baseScore + price,
            "Max" => Math.Max(baseScore, price),
            _ => throw new InvalidOperationException($"Unknown fixture function-score mode '{mode}'.")
        };

    private static string[] ReadJsonStrings(JsonElement value)
        => value.EnumerateArray().Select(static item => item.GetString()!).ToArray();

    private static Dictionary<string, float> ReadLeanScores(
        Rowles.LeanCorpus.Search.Scoring.TopDocs results,
        string[] logicalIds,
        out string[] rankedIds)
    {
        if (results.TotalHits != logicalIds.Length || results.ScoreDocs.Length != logicalIds.Length)
            throw new InvalidOperationException("BVAL-04 LeanCorpus fixture query did not return every logical document.");
        rankedIds = results.ScoreDocs.Select(scoreDoc => logicalIds[scoreDoc.DocId]).ToArray();
        return results.ScoreDocs.ToDictionary(scoreDoc => logicalIds[scoreDoc.DocId],
            static scoreDoc => scoreDoc.Score, StringComparer.Ordinal);
    }

    private static Dictionary<string, float> ReadLuceneScores(
        Lucene.Net.Search.TopDocs results,
        string[] logicalIds,
        out string[] rankedIds)
    {
        if (results.TotalHits != logicalIds.Length || results.ScoreDocs.Length != logicalIds.Length)
            throw new InvalidOperationException("BVAL-04 Lucene.NET fixture query did not return every logical document.");
        rankedIds = results.ScoreDocs.Select(scoreDoc => logicalIds[scoreDoc.Doc]).ToArray();
        return results.ScoreDocs.ToDictionary(scoreDoc => logicalIds[scoreDoc.Doc],
            static scoreDoc => scoreDoc.Score, StringComparer.Ordinal);
    }

    private static void AssertFixtureBaseScores(
        Dictionary<string, float> actual,
        string[] expectedIds,
        double expectedScore,
        string engine)
    {
        foreach (string id in expectedIds)
            if (!actual.TryGetValue(id, out float score) || !NearlyEqual(score, expectedScore))
                throw new InvalidOperationException($"BVAL-04 {engine} base score for {id} was {score}, expected {expectedScore}.");
    }

    private static void AssertFixtureScores(
        Dictionary<string, double> expected,
        Dictionary<string, float> actual,
        string mode,
        string engine)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException($"BVAL-04 {engine} {mode} score map has an unexpected document count.");
        foreach ((string id, double expectedScore) in expected)
            if (!actual.TryGetValue(id, out float actualScore) || !NearlyEqual(actualScore, expectedScore))
                throw new InvalidOperationException(
                    $"BVAL-04 {engine} {mode} score for {id} was {actualScore}, expected {expectedScore}.");
    }

    private static void AssertFixtureTopK(
        string[] expectedIds,
        string[] actualIds,
        string mode,
        string engine)
    {
        if (!expectedIds.SequenceEqual(actualIds, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"BVAL-04 {engine} {mode} top-K IDs differ. Expected=[{string.Join(",", expectedIds)}]; actual=[{string.Join(",", actualIds)}].");
    }

    private static bool NearlyEqual(double first, double second)
        => Math.Abs(first - second) <= 1e-5 * Math.Max(1d, Math.Max(Math.Abs(first), Math.Abs(second)));

    private sealed record FunctionScoreFixtureSegment(string Id, FunctionScoreFixtureDocument[] Documents);

    private sealed record FunctionScoreFixtureDocument(string Id, double? Price, bool PriceDocValuePresent);

    private void BuildLuceneIndex((string Body, double Price)[] docs)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-funcscore-{Guid.NewGuid():N}");
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
            doc.Add(new LuceneDoubleField("price", docs[i].Price,
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
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-funcscore-{Guid.NewGuid():N}");
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
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }
}
