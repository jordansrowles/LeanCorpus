using System.Collections;
using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexSearcherConfig = Rowles.LeanCorpus.Search.Searcher.IndexSearcherConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="MoreLikeThisQuery"/> against Lucene.NET <c>MoreLikeThis</c>
/// across <c>MaxQueryTerms</c> and <c>MinDocFreq</c> settings. Both indexes store term vectors.
/// <para>
/// Cold extraction paths clear only the LeanCorpus MLT term cache before each invocation,
/// matching Lucene.NET's per-invocation extraction and query construction. Separate
/// prebuilt-query and warm-cache paths have their own categories and are not ratioed
/// against the cold extraction baseline.
/// </para>
/// </summary>
[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class MoreLikeThisBenchmarks
{
    private const int TopN = 25;
    private const int SourceDocId = 100;
    private const string Bval03FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-03-morelikethis-cache-boundaries.json";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo MltCacheField = typeof(LeanIndexSearcher).GetField("_mltCache", PrivateInstance)
        ?? throw new InvalidOperationException("LeanCorpus MLT cache field was not found.");
    private static readonly FieldInfo MltCacheCountField = typeof(LeanIndexSearcher).GetField("_mltCacheCount", PrivateInstance)
        ?? throw new InvalidOperationException("LeanCorpus MLT cache count field was not found.");

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Maximum query terms extracted from the source document.</summary>
    public int MaxQueryTerms { get; set; } = 25;

    // LeanCorpus state
    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private LeanIndexSearcher? _leanWandSearcher;

    // Lucene.NET state
    private string _luceneIndexPath = string.Empty;
    private Lucene.Net.Store.MMapDirectory? _luceneDirectory;
    private Lucene.Net.Analysis.Standard.StandardAnalyzer? _luceneAnalyzer;
    private Lucene.Net.Index.DirectoryReader? _luceneReader;
    private Lucene.Net.Search.IndexSearcher? _luceneSearcher;
    private string _bval03LeanIndexPath = string.Empty;
    private LeanMMapDirectory? _bval03LeanDirectory;
    private LeanIndexSearcher? _bval03LeanSearcher;
    private string _bval03LuceneIndexPath = string.Empty;
    private Lucene.Net.Store.MMapDirectory? _bval03LuceneDirectory;
    private Lucene.Net.Analysis.Standard.StandardAnalyzer? _bval03LuceneAnalyzer;
    private Lucene.Net.Index.DirectoryReader? _bval03LuceneReader;
    private Lucene.Net.Search.IndexSearcher? _bval03LuceneSearcher;
    private Rowles.LeanCorpus.Search.Query _bval03LeanPrebuiltQuery = null!;
    private Lucene.Net.Search.Query _bval03LucenePrebuiltQuery = null!;
    private string[] _bval03DocumentIds = [];
    private int _bval03TopN;
    private int _bval03MinTermFreq;
    private int _bval03MinDocFreq;
    private int _bval03MinWordLength;
    private int _bval03MaxQueryTerms;
    private bool _bval03BoostByScore;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
        BuildLuceneIndex(documents);
        ValidateBval03Fixture();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        _leanWandSearcher?.Dispose();
        _bval03LeanSearcher?.Dispose();
        _bval03LeanDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_bval03LeanIndexPath) && IODirectory.Exists(_bval03LeanIndexPath))
            IODirectory.Delete(_bval03LeanIndexPath, recursive: true);
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _bval03LuceneReader?.Dispose();
        _bval03LuceneAnalyzer?.Dispose();
        _bval03LuceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_bval03LuceneIndexPath) && IODirectory.Exists(_bval03LuceneIndexPath))
            IODirectory.Delete(_bval03LuceneIndexPath, recursive: true);
        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    // --- Cold extraction + query construction + search ---

    [Benchmark(Description = "LeanCorpus DataForge MLT cold extraction + query + search")]
    [BenchmarkCategory("Unqualified DataForge MLT Default Cold")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_DefaultParams()
        => _leanSearcher!.Search(CreateLeanQuery(minTermFreq: 1, minDocFreq: 1, boostByScore: true), TopN).TotalHits;

    [Benchmark(Description = "Lucene.NET DataForge MLT cold extraction + query + search")]
    [BenchmarkCategory("Unqualified DataForge MLT Default Cold")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_DefaultParams()
        => SearchLuceneMlt(CreateLuceneQuery(minTermFreq: 1, minDocFreq: 1, applyBoost: true));

    [Benchmark(Description = "LeanCorpus DataForge MLT high-min-frequency cold extraction + query + search")]
    [BenchmarkCategory("Unqualified DataForge MLT High MinDocFreq Cold")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_HighMinDocFreq()
        => _leanSearcher!.Search(CreateLeanQuery(minTermFreq: 2, minDocFreq: 5, boostByScore: true), TopN).TotalHits;

    [Benchmark(Description = "Lucene.NET DataForge MLT high-min-frequency cold extraction + query + search")]
    [BenchmarkCategory("Unqualified DataForge MLT High MinDocFreq Cold")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_HighMinDocFreq()
        => SearchLuceneMlt(CreateLuceneQuery(minTermFreq: 2, minDocFreq: 5, applyBoost: true));

    [Benchmark(Description = "LeanCorpus DataForge MLT no-boost cold extraction + query + search")]
    [BenchmarkCategory("Unqualified DataForge MLT No Boost Cold")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_NoBoost()
        => _leanSearcher!.Search(CreateLeanQuery(minTermFreq: 1, minDocFreq: 1, boostByScore: false), TopN).TotalHits;

    [Benchmark(Description = "Lucene.NET DataForge MLT no-boost cold extraction + query + search")]
    [BenchmarkCategory("Unqualified DataForge MLT No Boost Cold")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_NoBoost()
        => SearchLuceneMlt(CreateLuceneQuery(minTermFreq: 1, minDocFreq: 1, applyBoost: false));

    [Benchmark(Description = "LeanCorpus BVAL-03 fixture cold extraction + query + search")]
    [BenchmarkCategory("BVAL-03 Fixture Cold Extraction")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_FixtureColdExtraction()
        => _bval03LeanSearcher!.Search(CreateBval03LeanQuery(), _bval03TopN).TotalHits;

    [Benchmark(Description = "Lucene.NET BVAL-03 fixture cold extraction + query + search")]
    [BenchmarkCategory("BVAL-03 Fixture Cold Extraction")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_FixtureColdExtraction()
        => SearchBval03LuceneMlt();

    [IterationSetup(Target = nameof(LeanCorpus_MoreLikeThisQuery_FixtureColdExtraction))]
    public void ResetFixtureLeanMltCache() => ResetMltCacheAndVerifyEmpty(_bval03LeanSearcher!);

    // Iteration setup is outside the timed operation and resets the LeanCorpus
    // cache before the single measured invocation in that iteration.
    [IterationSetup(Target = nameof(LeanCorpus_MoreLikeThisQuery_DefaultParams))]
    public void ResetDefaultLeanMltCache() => ResetMltCacheAndVerifyEmpty(_leanSearcher!);

    [IterationSetup(Target = nameof(LeanCorpus_MoreLikeThisQuery_HighMinDocFreq))]
    public void ResetHighFrequencyLeanMltCache() => ResetMltCacheAndVerifyEmpty(_leanSearcher!);

    [IterationSetup(Target = nameof(LeanCorpus_MoreLikeThisQuery_NoBoost))]
    public void ResetNoBoostLeanMltCache() => ResetMltCacheAndVerifyEmpty(_leanSearcher!);

    // --- Prebuilt query search ---

    [Benchmark(Description = "LeanCorpus BVAL-03 fixture prebuilt query search")]
    [BenchmarkCategory("BVAL-03 Fixture Prebuilt Query Search")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_FixturePrebuiltSearch()
        => SearchLeanPrebuilt(_bval03LeanSearcher!, _bval03LeanPrebuiltQuery, _bval03TopN, sourceDocId: 0);

    [Benchmark(Description = "Lucene.NET BVAL-03 fixture prebuilt query search")]
    [BenchmarkCategory("BVAL-03 Fixture Prebuilt Query Search")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MoreLikeThis_FixturePrebuiltSearch()
        => SearchLucenePrebuilt(_bval03LuceneSearcher!, _bval03LucenePrebuiltQuery, _bval03TopN, sourceDocId: 0);

    // --- Engine-specific warm LeanCorpus term-cache hit ---

    [Benchmark(Description = "LeanCorpus BVAL-03 fixture warm term-cache hit + query + search")]
    [BenchmarkCategory("BVAL-03 Fixture Warm Cache (LeanCorpus only)")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_FixtureWarmCacheHit()
        => _bval03LeanSearcher!.Search(CreateBval03LeanQuery(), _bval03TopN).TotalHits;

    [IterationSetup(Target = nameof(LeanCorpus_MoreLikeThisQuery_FixtureWarmCacheHit))]
    public void SeedWarmLeanMltCache()
    {
        ResetMltCacheAndVerifyEmpty(_bval03LeanSearcher!);
        _ = _bval03LeanSearcher!.Search(CreateBval03LeanQuery(), _bval03TopN);
        if (ReadMltCacheEntryCount(_bval03LeanSearcher!) != 1)
            throw new InvalidOperationException("BVAL-03 warm-cache setup did not seed exactly one MLT entry.");
    }

    // --- WAND's engine-specific warm cache path ---

    [Benchmark(Description = "LeanCorpus DataForge MLT WAND warm term-cache hit + query + search")]
    [BenchmarkCategory("Unqualified DataForge MLT WAND Warm Cache")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_MoreLikeThisQuery_Wand_DefaultParams()
        => _leanWandSearcher!.Search(CreateLeanQuery(minTermFreq: 1, minDocFreq: 1, boostByScore: true), TopN).TotalHits;

    [IterationSetup(Target = nameof(LeanCorpus_MoreLikeThisQuery_Wand_DefaultParams))]
    public void SeedWarmLeanWandMltCache()
    {
        ResetMltCacheAndVerifyEmpty(_leanWandSearcher!);
        _ = _leanWandSearcher!.Search(CreateLeanQuery(minTermFreq: 1, minDocFreq: 1, boostByScore: true), TopN);
        if (ReadMltCacheEntryCount(_leanWandSearcher!) != 1)
            throw new InvalidOperationException("WAND warm-cache setup did not seed exactly one MLT entry.");
    }

    private MoreLikeThisQuery CreateBval03LeanQuery()
        => CreateLeanQuery(_bval03MinTermFreq, _bval03MinDocFreq, _bval03BoostByScore,
            sourceDocId: 0, maxQueryTerms: _bval03MaxQueryTerms, minWordLength: _bval03MinWordLength);

    private int SearchBval03LuceneMlt()
    {
        Lucene.Net.Search.Query query = CreateLuceneQueryForReader(
            _bval03LuceneReader!, _bval03MinTermFreq, _bval03MinDocFreq,
            _bval03MinWordLength, _bval03MaxQueryTerms, _bval03BoostByScore, sourceDocId: 0);
        Lucene.Net.Search.TopDocs results = _bval03LuceneSearcher!.Search(query, _bval03TopN);
        return ExcludeSourceHit(results.TotalHits,
            results.ScoreDocs.Any(scoreDoc => scoreDoc.Doc == 0));
    }

    private MoreLikeThisQuery CreateLeanQuery(
        int minTermFreq,
        int minDocFreq,
        bool boostByScore,
        int? sourceDocId = null,
        int? maxQueryTerms = null,
        int minWordLength = 3)
        => new(sourceDocId ?? SourceDocId, ["body"], new MoreLikeThisParameters
        {
            MaxQueryTerms = maxQueryTerms ?? MaxQueryTerms,
            MinTermFreq = minTermFreq,
            MinDocFreq = minDocFreq,
            MinWordLength = minWordLength,
            BoostByScore = boostByScore
        });

    private Lucene.Net.Search.Query CreateLuceneQuery(
        int minTermFreq,
        int minDocFreq,
        bool applyBoost,
        int? sourceDocId = null,
        int? maxQueryTerms = null)
    {
        var mlt = new Lucene.Net.Queries.Mlt.MoreLikeThis(_luceneReader!);
        mlt.MinTermFreq = minTermFreq;
        mlt.MinDocFreq = minDocFreq;
        mlt.MinWordLen = 3;
        mlt.MaxQueryTerms = maxQueryTerms ?? MaxQueryTerms;
        mlt.ApplyBoost = applyBoost;
        mlt.FieldNames = ["body"];
        return mlt.Like(sourceDocId ?? SourceDocId);
    }

    private int SearchLuceneMlt(Lucene.Net.Search.Query query)
    {
        Lucene.Net.Search.TopDocs results = _luceneSearcher!.Search(query, TopN);
        return ExcludeSourceHit(results.TotalHits,
            results.ScoreDocs.Any(scoreDoc => scoreDoc.Doc == SourceDocId));
    }

    private static int SearchLeanPrebuilt(
        LeanIndexSearcher searcher,
        Rowles.LeanCorpus.Search.Query query,
        int topN,
        int sourceDocId)
    {
        Rowles.LeanCorpus.Search.Scoring.TopDocs results = searcher.Search(query, topN);
        return ExcludeSourceHit(results.TotalHits,
            results.ScoreDocs.Any(scoreDoc => scoreDoc.DocId == sourceDocId));
    }

    private static int SearchLucenePrebuilt(
        Lucene.Net.Search.IndexSearcher searcher,
        Lucene.Net.Search.Query query,
        int topN,
        int sourceDocId)
    {
        Lucene.Net.Search.TopDocs results = searcher.Search(query, topN);
        return ExcludeSourceHit(results.TotalHits,
            results.ScoreDocs.Any(scoreDoc => scoreDoc.Doc == sourceDocId));
    }

    private static int ExcludeSourceHit(int totalHits, bool sourceWasReturned)
        => sourceWasReturned ? Math.Max(0, totalHits - 1) : totalHits;

    private void ValidateBval03Fixture()
    {
        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            Bval03FixturePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(),
                "bval-03-morelikethis-cache-boundaries", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidOperationException($"BVAL-03 fixture metadata is invalid: {Bval03FixturePath}.");

        JsonElement input = root.GetProperty("input");
        JsonElement source = input.GetProperty("sourceDocument");
        var fixtureDocuments = new List<(string Id, string Text)>
        {
            (source.GetProperty("id").GetString()!, source.GetProperty("text").GetString()!)
        };
        fixtureDocuments.AddRange(input.GetProperty("documents").EnumerateArray()
            .Select(static item => (item.GetProperty("id").GetString()!, item.GetProperty("text").GetString()!)));
        if (fixtureDocuments.Select(static item => item.Id).Distinct(StringComparer.Ordinal).Count() != fixtureDocuments.Count)
            throw new InvalidOperationException("BVAL-03 fixture document IDs must be unique.");

        JsonElement parameters = input.GetProperty("parameters");
        int minTermFreq = parameters.GetProperty("minTermFreq").GetInt32();
        int minDocFreq = parameters.GetProperty("minDocFreq").GetInt32();
        int minWordLength = parameters.GetProperty("minWordLength").GetInt32();
        int maxQueryTerms = parameters.GetProperty("maxQueryTerms").GetInt32();
        bool boostByScore = parameters.GetProperty("boostByScore").GetBoolean();
        int topN = input.GetProperty("topN").GetInt32();
        _bval03DocumentIds = fixtureDocuments.Select(static item => item.Id).ToArray();
        _bval03TopN = topN;
        _bval03MinTermFreq = minTermFreq;
        _bval03MinDocFreq = minDocFreq;
        _bval03MinWordLength = minWordLength;
        _bval03MaxQueryTerms = maxQueryTerms;
        _bval03BoostByScore = boostByScore;
        JsonElement expected = root.GetProperty("expected");
        string[] expectedTerms = ReadJsonStrings(expected.GetProperty("coldExtractionTerms"));
        string[] expectedWarmTerms = ReadJsonStrings(expected.GetProperty("warmExtractionTerms"));
        string[] expectedColdIds = ReadJsonStrings(expected.GetProperty("coldLogicalCandidateIds"));
        string[] expectedWarmIds = ReadJsonStrings(expected.GetProperty("warmLogicalCandidateIds"));
        if (!expected.GetProperty("cacheStateMustBeExplicit").GetBoolean()
            || !expected.GetProperty("uncachedComparisonMustIncludeExtractionOnBothEngines").GetBoolean()
            || minTermFreq < 1 || minDocFreq < 1 || minWordLength < 1 || maxQueryTerms < 1 || topN < expectedColdIds.Length)
            throw new InvalidOperationException("BVAL-03 fixture does not define valid explicit extraction and search boundaries.");

        _bval03LeanIndexPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-03-mlt-lean");
        _bval03LuceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-03-mlt-lucene");
        _bval03LeanDirectory = new LeanMMapDirectory(_bval03LeanIndexPath);
        using (var writer = new IndexWriter(_bval03LeanDirectory, new IndexWriterConfig { StoreTermVectors = true }))
        {
            foreach ((string id, string text) in fixtureDocuments)
            {
                var leanDocument = new LeanDocument();
                leanDocument.Add(new LeanStringField("id", id));
                leanDocument.Add(new LeanTextField("body", text));
                writer.AddDocument(leanDocument);
            }
            writer.Commit();
        }

        _bval03LeanSearcher = new LeanIndexSearcher(_bval03LeanDirectory,
            new LeanIndexSearcherConfig { EnableQueryCache = false });
        var fixtureLeanQuery = CreateBval03LeanQuery();
        if (ReadMltCacheEntryCount(_bval03LeanSearcher) != 0)
            throw new InvalidOperationException("BVAL-03 LeanCorpus fixture did not start with an empty MLT cache.");

        Rowles.LeanCorpus.Search.Scoring.TopDocs leanColdResults = _bval03LeanSearcher.Search(fixtureLeanQuery, topN);
        string[] leanColdTerms = ReadMltCacheTerms(_bval03LeanSearcher);
        if (ReadMltCacheEntryCount(_bval03LeanSearcher) != 1)
            throw new InvalidOperationException("BVAL-03 LeanCorpus cold extraction did not produce one cache entry.");
        string[] leanColdIds = ReadLeanLogicalIds(leanColdResults, sourceDocId: 0, fixtureDocuments.Count,
            _bval03DocumentIds);

        Rowles.LeanCorpus.Search.Scoring.TopDocs leanWarmResults = _bval03LeanSearcher.Search(fixtureLeanQuery, topN);
        string[] leanWarmTerms = ReadMltCacheTerms(_bval03LeanSearcher);
        string[] leanWarmIds = ReadLeanLogicalIds(leanWarmResults, sourceDocId: 0, fixtureDocuments.Count,
            _bval03DocumentIds);
        if (ReadMltCacheEntryCount(_bval03LeanSearcher) != 1)
            throw new InvalidOperationException("BVAL-03 LeanCorpus warm lookup changed the MLT cache entry count.");

        _bval03LuceneDirectory = new Lucene.Net.Store.MMapDirectory(new System.IO.DirectoryInfo(_bval03LuceneIndexPath));
        _bval03LuceneAnalyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
        using (var writer = new Lucene.Net.Index.IndexWriter(_bval03LuceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, _bval03LuceneAnalyzer)))
        {
            foreach ((string id, string text) in fixtureDocuments)
            {
                var fieldType = new Lucene.Net.Documents.FieldType
                {
                    IsIndexed = true,
                    IsTokenized = true,
                    IsStored = false,
                    StoreTermVectors = true
                };
                var luceneDocument = new Lucene.Net.Documents.Document
                {
                    new Lucene.Net.Documents.StringField("id", id, Lucene.Net.Documents.Field.Store.NO),
                    new Lucene.Net.Documents.Field("body", text, fieldType)
                };
                writer.AddDocument(luceneDocument);
            }
            writer.Commit();
        }

        _bval03LuceneReader = Lucene.Net.Index.DirectoryReader.Open(_bval03LuceneDirectory);
        _bval03LuceneSearcher = new Lucene.Net.Search.IndexSearcher(_bval03LuceneReader);
        Lucene.Net.Search.Query luceneColdQuery = CreateLuceneQueryForReader(
            _bval03LuceneReader, minTermFreq, minDocFreq, minWordLength, maxQueryTerms, boostByScore, sourceDocId: 0);
            string[] luceneColdTerms = ReadLuceneMltTerms(luceneColdQuery);
            Lucene.Net.Search.TopDocs luceneColdResults = _bval03LuceneSearcher.Search(luceneColdQuery, topN);
            string[] luceneColdIds = ReadLuceneLogicalIds(luceneColdResults, sourceDocId: 0,
                fixtureDocuments.Count, _bval03DocumentIds);

            _bval03LucenePrebuiltQuery = BuildLuceneBooleanQuery(expectedTerms);
            _bval03LeanPrebuiltQuery = BuildLeanBooleanQuery(expectedTerms);
            string[] leanPrebuiltIds = ReadLeanLogicalIds(
                _bval03LeanSearcher.Search(_bval03LeanPrebuiltQuery, topN), sourceDocId: 0,
                fixtureDocuments.Count, _bval03DocumentIds);
            string[] lucenePrebuiltIds = ReadLuceneLogicalIds(
                _bval03LuceneSearcher.Search(_bval03LucenePrebuiltQuery, topN), sourceDocId: 0,
                fixtureDocuments.Count, _bval03DocumentIds);

            AssertSameTerms(expectedTerms, leanColdTerms, "BVAL-03 LeanCorpus cold extraction");
            AssertSameTerms(expectedWarmTerms, leanWarmTerms, "BVAL-03 LeanCorpus warm extraction");
            AssertSameTerms(expectedTerms, luceneColdTerms, "BVAL-03 Lucene.NET cold extraction");
            AssertSameLogicalIds(expectedColdIds, leanColdIds, "BVAL-03 LeanCorpus cold search");
            AssertSameLogicalIds(expectedColdIds, luceneColdIds, "BVAL-03 Lucene.NET cold search");
            AssertSameLogicalIds(expectedWarmIds, leanWarmIds, "BVAL-03 LeanCorpus warm search");
            AssertSameLogicalIds(expectedWarmIds, leanPrebuiltIds, "BVAL-03 LeanCorpus prebuilt query search");
            AssertSameLogicalIds(expectedWarmIds, lucenePrebuiltIds, "BVAL-03 Lucene.NET prebuilt query search");
            if (!leanColdTerms.SequenceEqual(luceneColdTerms, StringComparer.Ordinal))
                throw new InvalidOperationException("BVAL-03 cold extraction selected different logical terms across engines.");
            AssertSameLogicalIds(leanColdIds, leanWarmIds, "BVAL-03 cold versus warm LeanCorpus results");
            AssertSameLogicalIds(leanPrebuiltIds, lucenePrebuiltIds, "BVAL-03 equivalent prebuilt-query results");
    }

    private static Lucene.Net.Search.Query CreateLuceneQueryForReader(
        Lucene.Net.Index.IndexReader reader,
        int minTermFreq,
        int minDocFreq,
        int minWordLength,
        int maxQueryTerms,
        bool applyBoost,
        int sourceDocId)
    {
        var mlt = new Lucene.Net.Queries.Mlt.MoreLikeThis(reader)
        {
            MinTermFreq = minTermFreq,
            MinDocFreq = minDocFreq,
            MinWordLen = minWordLength,
            MaxQueryTerms = maxQueryTerms,
            ApplyBoost = applyBoost,
            FieldNames = ["body"]
        };
        return mlt.Like(sourceDocId);
    }

    private static string[] ReadJsonStrings(JsonElement value)
        => value.EnumerateArray().Select(static item => item.GetString()!).ToArray();

    private static int ReadMltCacheEntryCount(LeanIndexSearcher searcher)
    {
        if (MltCacheField.GetValue(searcher) is not IDictionary cache)
            return 0;
        return cache.Count;
    }

    private static string[] ReadMltCacheTerms(LeanIndexSearcher searcher)
    {
        if (MltCacheField.GetValue(searcher) is not IDictionary cache || cache.Count != 1)
            throw new InvalidOperationException("Expected exactly one LeanCorpus MLT cache entry.");
        if (cache.Values is not IEnumerable values)
            throw new InvalidOperationException("LeanCorpus MLT cache values are not enumerable.");
        object? entry = values.Cast<object?>().SingleOrDefault();
        if (entry is not IEnumerable terms)
            throw new InvalidOperationException("LeanCorpus MLT cache entry is not enumerable.");

        return terms.Cast<object?>()
            .Select(static item => item?.GetType().GetField("Item2")?.GetValue(item) as string
                ?? throw new InvalidOperationException("LeanCorpus MLT cache entry did not expose its term value."))
            .OrderBy(static term => term, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ResetMltCache(LeanIndexSearcher searcher)
    {
        MltCacheField.SetValue(searcher, null);
        MltCacheCountField.SetValue(searcher, 0);
    }

    private static void ResetMltCacheAndVerifyEmpty(LeanIndexSearcher searcher)
    {
        ResetMltCache(searcher);
        if (ReadMltCacheEntryCount(searcher) != 0)
            throw new InvalidOperationException("LeanCorpus MLT iteration setup did not produce an empty term cache.");
    }

    private static string[] ReadLuceneMltTerms(Lucene.Net.Search.Query query)
    {
        var terms = new HashSet<string>(StringComparer.Ordinal);
        Visit(query);
        return terms.OrderBy(static term => term, StringComparer.Ordinal).ToArray();

        void Visit(Lucene.Net.Search.Query current)
        {
            if (current is Lucene.Net.Search.TermQuery termQuery)
            {
                terms.Add(termQuery.Term.Text);
                return;
            }

            if (current is Lucene.Net.Search.BooleanQuery booleanQuery)
            {
                foreach (Lucene.Net.Search.BooleanClause clause in booleanQuery.Clauses)
                    Visit(clause.Query);
                return;
            }

            throw new InvalidOperationException($"Unexpected Lucene.NET MLT query node: {current.GetType().FullName}.");
        }
    }

    private static Rowles.LeanCorpus.Search.Query BuildLeanBooleanQuery(IEnumerable<string> terms)
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        foreach (string term in terms)
            builder.Add(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", term),
                Rowles.LeanCorpus.Search.Occur.Should);
        return builder.Build();
    }

    private static Lucene.Net.Search.Query BuildLuceneBooleanQuery(IEnumerable<string> terms)
    {
        var builder = new Lucene.Net.Search.BooleanQuery();
        foreach (string term in terms)
            builder.Add(new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", term)),
                Lucene.Net.Search.Occur.SHOULD);
        return builder;
    }

    private static string[] ReadLeanLogicalIds(
        Rowles.LeanCorpus.Search.Scoring.TopDocs results,
        int sourceDocId,
        int documentCount,
        string[]? ids = null)
        => results.ScoreDocs
            .Select(static scoreDoc => scoreDoc.DocId)
            .Where(docId => docId != sourceDocId && docId >= 0 && docId < documentCount)
            .Select(docId => ids is null ? docId.ToString(System.Globalization.CultureInfo.InvariantCulture) : ids[docId])
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

    private static string[] ReadLuceneLogicalIds(
        Lucene.Net.Search.TopDocs results,
        int sourceDocId,
        int documentCount,
        string[]? ids = null)
        => results.ScoreDocs
            .Select(static scoreDoc => scoreDoc.Doc)
            .Where(docId => docId != sourceDocId && docId >= 0 && docId < documentCount)
            .Select(docId => ids is null ? docId.ToString(System.Globalization.CultureInfo.InvariantCulture) : ids[docId])
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

    private static void AssertSameTerms(string[] expected, string[] actual, string label)
    {
        if (!expected.OrderBy(static term => term, StringComparer.Ordinal)
                .SequenceEqual(actual.OrderBy(static term => term, StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException($"{label} terms differ. Expected=[{string.Join(",", expected)}]; actual=[{string.Join(",", actual)}].");
    }

    private static void AssertSameLogicalIds(string[] expected, string[] actual, string label)
    {
        string[] orderedExpected = expected.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        string[] orderedActual = actual.OrderBy(static id => id, StringComparer.Ordinal).ToArray();
        if (!orderedExpected.SequenceEqual(orderedActual, StringComparer.Ordinal))
            throw new InvalidOperationException($"{label} logical IDs differ. Expected=[{string.Join(",", orderedExpected)}]; actual=[{string.Join(",", orderedActual)}].");
    }

    // --- Index builders ---

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-mlt-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new IndexWriter(
            _leanDirectory,
            new IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256,
                StoreTermVectors = true
            });
        for (int i = 0; i < documents.Length; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory,
            new LeanIndexSearcherConfig { EnableQueryCache = false });
        _leanWandSearcher = new LeanIndexSearcher(_leanDirectory,
            new LeanIndexSearcherConfig { EnableBlockMaxWand = true, EnableQueryCache = false });
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-mlt-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);

        _luceneDirectory = new Lucene.Net.Store.MMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, _luceneAnalyzer)))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                // Use FieldType to enable term vectors without deprecated API.
                var fieldType = new Lucene.Net.Documents.FieldType
                {
                    IsIndexed = true,
                    IsTokenized = true,
                    IsStored = false,
                    StoreTermVectors = true
                };
                var doc = new Lucene.Net.Documents.Document
                {
                    new Lucene.Net.Documents.StringField(
                        "id",
                        i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Lucene.Net.Documents.Field.Store.NO),
                    new Lucene.Net.Documents.Field("body", documents[i], fieldType)
                };
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _luceneReader = Lucene.Net.Index.DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new Lucene.Net.Search.IndexSearcher(_luceneReader);
    }
}
