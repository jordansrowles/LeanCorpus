using System.Globalization;
using System.Text.Json;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Shared, lazily-built standard benchmark index for search-parity suites.
///
/// Suites that use the identical <c>id</c> + <c>body</c> schema (TermQuery,
/// BooleanQuery, PhraseQuery, PrefixQuery, FuzzyQuery, WildcardQuery, RegexpQuery,
/// DisjunctionMaxQuery, MultiPhraseQuery, SpanQuery, TermInSetQuery, QueryCache,
/// and SearcherManager) consume this fixture instead of each building their own
/// index in [GlobalSetup]. Similarity uses the shared LeanCorpus index plus
/// Lucene.NET indexes written separately for each scoring model.
///
/// The runner builds the LeanCorpus and Lucene.NET indices once. BenchmarkDotNet
/// child processes then open the immutable fixture paths read-only.
/// </summary>
internal static class SharedStandardIndex
{
    private const string PreparedDocCountVariable = "BENCH_STANDARD_INDEX_DOC_COUNT";
    private const string PreparedLeanPathVariable = "BENCH_STANDARD_LEAN_INDEX";
    private const string PreparedLucenePathVariable = "BENCH_STANDARD_LUCENE_INDEX";
    private const string PreparedBm25LucenePathVariable = "BENCH_STANDARD_BM25_LUCENE_INDEX";
    private const string PreparedDirichletLucenePathVariable = "BENCH_STANDARD_DIRICHLET_LUCENE_INDEX";
    private const string PreparedJelinekMercerLucenePathVariable = "BENCH_STANDARD_JELINEK_MERCER_LUCENE_INDEX";
    private const string Bval05FixturePath = ".config/devops/benchmark-correctness-fixtures/bval-05-matched-bm25-index-and-query.json";
    private const string Bval05SimilarityProfile = "BM25(k1=1.2,b=0.75)";
    private const string Bval05SimilarityCommitDataKey = "leancorpus.benchmark.similarity";
    private const string Bval08ClassicTfIdfProfile = "ClassicTFIDF";
    private const string Bval08DirichletProfile = "Dirichlet(mu=2000)";
    private const string Bval08JelinekMercerProfile = "JelinekMercer(lambda=0.1)";

    private static readonly Lock Gate = new();
    private static bool _initialised;
    private static bool _ownsIndexDirectories;
    private static bool _ownsSimilarityIndexDirectories;
    private static int _docCount;

    /// <summary>Cached document bodies, shared across all standard-search suites.</summary>
    private static string[] _documents = [];

    // ── LeanCorpus ──

    /// <summary>Path to the on-disk LeanCorpus MMapDirectory index.</summary>
    private static string _leanIndexPath = string.Empty;

    private static LeanMMapDirectory? _leanDirectory;
    private static LeanIndexSearcher? _leanSearcher;

    // ── Lucene.NET ──

    private static string _luceneIndexPath = string.Empty;
    private static LuceneMMapDirectory? _luceneDirectory;
    private static StandardAnalyzer? _luceneAnalyzer;
    private static DirectoryReader? _luceneReader;
    private static LuceneIndexSearcher? _luceneSearcher;

    // The matched profile has BM25 norms written at index time and BM25 scoring
    // applied at query time. The default Lucene index above remains as shipped.
    private static string _luceneBm25IndexPath = string.Empty;
    private static LuceneMMapDirectory? _luceneBm25Directory;
    private static StandardAnalyzer? _luceneBm25Analyzer;
    private static DirectoryReader? _luceneBm25Reader;
    private static LuceneIndexSearcher? _luceneBm25Searcher;

    private static string _luceneDirichletIndexPath = string.Empty;
    private static string _luceneJelinekMercerIndexPath = string.Empty;

    // ── Public API ──

    /// <summary>Document count currently loaded.</summary>
    public static int DocCount => _docCount;

    /// <summary>True once initialised (any thread may read without a lock).</summary>
    public static bool IsInitialised => _initialised;

    /// <summary>
    /// Builds the standard fixture in the benchmark runner and exposes its paths
    /// to BenchmarkDotNet child processes. Similarity-specific Lucene indexes are
    /// created only when that workload is selected.
    /// </summary>
    public static void PrepareForRun(int docCount, bool includeSimilarityModelIndexes = false)
    {
        EnsureInitialised(docCount);
        if (includeSimilarityModelIndexes)
            EnsureLuceneSimilarityIndexes();
        Environment.SetEnvironmentVariable(
            PreparedDocCountVariable,
            docCount.ToString(CultureInfo.InvariantCulture));
        Environment.SetEnvironmentVariable(PreparedLeanPathVariable, _leanIndexPath);
        Environment.SetEnvironmentVariable(PreparedLucenePathVariable, _luceneIndexPath);
        Environment.SetEnvironmentVariable(PreparedBm25LucenePathVariable, _luceneBm25IndexPath);
        Environment.SetEnvironmentVariable(PreparedDirichletLucenePathVariable, _luceneDirichletIndexPath);
        Environment.SetEnvironmentVariable(PreparedJelinekMercerLucenePathVariable, _luceneJelinekMercerIndexPath);
    }

    /// <summary>
    /// Ensure the shared standard index is built for <paramref name="docCount"/> documents.
    /// Safe to call from multiple [GlobalSetup] methods — only the first call does work.
    /// </summary>
    public static void EnsureInitialised(int docCount)
    {
        if (_initialised && _docCount == docCount)
            return;

        lock (Gate)
        {
            if (_initialised && _docCount == docCount)
                return;

            // If already initialised with a different doc count, tear down first
            if (_initialised)
                Teardown();

            _docCount = docCount;

            if (TryOpenPreparedFixture(docCount))
            {
                _initialised = true;
                try
                {
                    ValidateBval05Fixture();
                }
                catch
                {
                    Teardown();
                    throw;
                }
                return;
            }

            _ownsIndexDirectories = true;
            _documents = BenchmarkData.BuildDocuments(docCount);

            // ── LeanCorpus index ──
            _leanIndexPath = Path.Combine(
                BenchmarkHelpers.TempRoot,
                $"leancorpus-shared-stdidx-{Guid.NewGuid():N}");
            Directory.CreateDirectory(_leanIndexPath);

            try
            {
                _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
                using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                    _leanDirectory,
                    new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig
                    {
                        MaxBufferedDocs = 10_000,
                        RamBufferSizeMB = 256
                    }))
                {
                    for (int i = 0; i < _documents.Length; i++)
                    {
                        var doc = new LeanDocument();
                        doc.Add(new LeanStringField("id",
                            i.ToString(CultureInfo.InvariantCulture)));
                        doc.Add(new LeanTextField("body", _documents[i]));
                        writer.AddDocument(doc);
                    }

                    writer.Commit();
                }

                _leanSearcher = new LeanIndexSearcher(_leanDirectory);
                RunSearchPreflight(docCount);

                // ── Lucene.NET index ──
                _luceneIndexPath = Path.Combine(
                    BenchmarkHelpers.TempRoot,
                    $"lucenenet-shared-stdidx-{Guid.NewGuid():N}");
                IODirectory.CreateDirectory(_luceneIndexPath);

                _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
                _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

                var luceneWriterConfig = new Lucene.Net.Index.IndexWriterConfig(
                    LuceneVersion.LUCENE_48, _luceneAnalyzer);
                ValidateBval08SimilarityProfile(luceneWriterConfig.Similarity, "classic-tfidf", "index writer");
                using (var luceneWriter = new Lucene.Net.Index.IndexWriter(
                    _luceneDirectory, luceneWriterConfig))
                {
                    luceneWriter.SetCommitData(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [Bval05SimilarityCommitDataKey] = Bval08ClassicTfIdfProfile
                    });

                    for (int i = 0; i < _documents.Length; i++)
                    {
                        var doc = new Lucene.Net.Documents.Document
                        {
                            new LuceneStringField("id",
                                i.ToString(CultureInfo.InvariantCulture),
                                Lucene.Net.Documents.Field.Store.NO),
                            new LuceneTextField("body", _documents[i],
                                Lucene.Net.Documents.Field.Store.NO)
                        };
                        luceneWriter.AddDocument(doc);
                    }

                    luceneWriter.Commit();
                }

                _luceneReader = DirectoryReader.Open(_luceneDirectory);
                ValidateBval08IndexProfile(_luceneReader, "classic-tfidf", "shared Classic TF-IDF index");
                _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
                BuildLuceneBm25Index();
            }
            catch
            {
                // Index build failed (e.g. disk full). Clean up partial
                // directories so they don't waste space for later suites.
                Teardown();
                throw;
            }

            _initialised = true;
            try
            {
                ValidateBval05Fixture();
            }
            catch
            {
                Teardown();
                throw;
            }
        }
    }

    private static void RunSearchPreflight(int docCount)
    {
        var identity = BenchmarkData.GetDatasetIdentity(docCount);
        var searchIdentity = string.Concat(
            "profile=", identity.ProfileId,
            " version=", identity.ProfileVersion?.ToString(CultureInfo.InvariantCulture) ?? "null",
            " seed=", identity.Seed?.ToString(CultureInfo.InvariantCulture) ?? "null",
            " count=", identity.RecordCount.ToString(CultureInfo.InvariantCulture),
            " contentSha256=", identity.ContentSha256);

        if (BenchmarkData.IsWikipediaReferenceMode)
            return;

        try
        {
            var searcher = _leanSearcher ?? throw new InvalidOperationException("The LeanCorpus searcher is unavailable.");
            Check("term said", new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "said"), minimumHits: 1);
            Check("term government", new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "government"), minimumHits: 1);
            Check("term people", new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "people"), minimumHits: 1);

            Check("phrase new york", new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", "new", "york"), minimumHits: 1);
            Check("phrase new york stock", new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", "new", "york", "stock"), minimumHits: 1);
            Check("phrase said government slop 2", new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", slop: 2, "said", "government"), minimumHits: 1);

            Check("prefix gov", new Rowles.LeanCorpus.Search.Queries.PrefixQuery("body", "gov"), minimumHits: 2);
            Check("prefix pres", new Rowles.LeanCorpus.Search.Queries.PrefixQuery("body", "pres"), minimumHits: 2);
            Check("prefix mark", new Rowles.LeanCorpus.Search.Queries.PrefixQuery("body", "mark"), minimumHits: 2);

            Check("wildcard gov*", new Rowles.LeanCorpus.Search.Queries.WildcardQuery("body", "gov*"), minimumHits: 2);
            Check("wildcard m*rket", new Rowles.LeanCorpus.Search.Queries.WildcardQuery("body", "m*rket"), minimumHits: 2);
            Check("wildcard pre*dent", new Rowles.LeanCorpus.Search.Queries.WildcardQuery("body", "pre*dent"), minimumHits: 2);

            Check("regexp gov.*ment", new Rowles.LeanCorpus.Search.Queries.RegexpQuery("body", "gov.*ment"), minimumHits: 2);
            Check("regexp mark.*", new Rowles.LeanCorpus.Search.Queries.RegexpQuery("body", "mark.*"), minimumHits: 2);
            Check("regexp .*nation.*", new Rowles.LeanCorpus.Search.Queries.RegexpQuery("body", ".*nation.*"), minimumHits: 2);

            Check("fuzzy marke/1", new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", "marke", 1), minimumHits: 1);
            Check("fuzzy goverment/1", new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", "goverment", 1), minimumHits: 1);
            Check("fuzzy presdnt/2", new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", "presdnt", 2), minimumHits: 1);
            Check("fuzzy econmic/1", new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", "econmic", 1), minimumHits: 1);

            var noHitCount = searcher.Search(
                new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", "zzzznomatch", 2),
                1).TotalHits;
            if (noHitCount != 0)
                throw new InvalidOperationException($"Fuzzy no-hit scenario returned {noHitCount} hits; expected zero.");

            foreach (var (original, _) in BenchmarkData.BuildMisspelledTerms())
                Check($"suggester original {original}", new Rowles.LeanCorpus.Search.Queries.TermQuery("body", original), minimumHits: 1);

            return;

            void Check(string scenario, Rowles.LeanCorpus.Search.Query query, int minimumHits)
            {
                var totalHits = searcher.Search(query, 1).TotalHits;
                if (totalHits < minimumHits)
                    throw new InvalidOperationException($"{scenario} returned {totalHits} hits; expected at least {minimumHits}.");
            }
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"DataForge search preflight failed ({searchIdentity}).", exception);
        }
    }

    private static bool TryOpenPreparedFixture(int docCount)
    {
        var preparedCount = Environment.GetEnvironmentVariable(PreparedDocCountVariable);
        var leanPath = Environment.GetEnvironmentVariable(PreparedLeanPathVariable);
        var lucenePath = Environment.GetEnvironmentVariable(PreparedLucenePathVariable);
        var bm25LucenePath = Environment.GetEnvironmentVariable(PreparedBm25LucenePathVariable);
        var dirichletLucenePath = Environment.GetEnvironmentVariable(PreparedDirichletLucenePathVariable);
        var jelinekMercerLucenePath = Environment.GetEnvironmentVariable(PreparedJelinekMercerLucenePathVariable);
        bool hasDirichletPath = !string.IsNullOrWhiteSpace(dirichletLucenePath);
        bool hasJelinekMercerPath = !string.IsNullOrWhiteSpace(jelinekMercerLucenePath);

        if (!int.TryParse(preparedCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            || count != docCount
            || string.IsNullOrWhiteSpace(leanPath)
            || string.IsNullOrWhiteSpace(lucenePath)
            || string.IsNullOrWhiteSpace(bm25LucenePath)
            || hasDirichletPath != hasJelinekMercerPath
            || !Directory.Exists(leanPath)
            || !Directory.Exists(lucenePath)
            || !Directory.Exists(bm25LucenePath)
            || (hasDirichletPath && !Directory.Exists(dirichletLucenePath))
            || (hasJelinekMercerPath && !Directory.Exists(jelinekMercerLucenePath)))
        {
            return false;
        }

        _leanIndexPath = leanPath;
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);

        _luceneIndexPath = lucenePath;
        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneReader = DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
        _luceneBm25IndexPath = bm25LucenePath;
        _luceneBm25Directory = new LuceneMMapDirectory(new DirectoryInfo(_luceneBm25IndexPath));
        _luceneBm25Reader = DirectoryReader.Open(_luceneBm25Directory);
        _luceneBm25Searcher = new LuceneIndexSearcher(_luceneBm25Reader)
        {
            Similarity = CreateBval05Bm25Similarity()
        };
        ValidateBval05Bm25Reader(_luceneBm25Reader, "prepared shared BM25 index");
        ValidateBval08IndexProfile(_luceneReader, "classic-tfidf", "prepared shared Classic TF-IDF index");
        _luceneDirichletIndexPath = dirichletLucenePath ?? string.Empty;
        _luceneJelinekMercerIndexPath = jelinekMercerLucenePath ?? string.Empty;
        _ownsIndexDirectories = false;
        return true;
    }

    private static void BuildLuceneBm25Index()
    {
        _luceneBm25IndexPath = Path.Combine(
            BenchmarkHelpers.TempRoot,
            $"lucenenet-shared-bm25-stdidx-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneBm25IndexPath);

        _luceneBm25Directory = new LuceneMMapDirectory(new DirectoryInfo(_luceneBm25IndexPath));
        _luceneBm25Analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var bm25Similarity = CreateBval05Bm25Similarity();
        var writerConfig = new Lucene.Net.Index.IndexWriterConfig(
            LuceneVersion.LUCENE_48, _luceneBm25Analyzer)
        {
            Similarity = bm25Similarity
        };
        ValidateBval05Bm25Profile(writerConfig.Similarity, "index writer");

        using (var writer = new Lucene.Net.Index.IndexWriter(_luceneBm25Directory, writerConfig))
        {
            writer.SetCommitData(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Bval05SimilarityCommitDataKey] = Bval05SimilarityProfile
            });

            for (int i = 0; i < _documents.Length; i++)
            {
                var document = new Lucene.Net.Documents.Document
                {
                    new LuceneStringField("id",
                        i.ToString(CultureInfo.InvariantCulture),
                        Lucene.Net.Documents.Field.Store.NO),
                    new LuceneTextField("body", _documents[i],
                        Lucene.Net.Documents.Field.Store.NO)
                };
                writer.AddDocument(document);
            }

            writer.Commit();
        }

        _luceneBm25Reader = DirectoryReader.Open(_luceneBm25Directory);
        _luceneBm25Searcher = new LuceneIndexSearcher(_luceneBm25Reader)
        {
            Similarity = bm25Similarity
        };
        ValidateBval05Bm25Profile(_luceneBm25Searcher.Similarity, "query searcher");
        ValidateBval05Bm25Reader(_luceneBm25Reader, "shared BM25 index");
        ValidateBval08IndexProfile(_luceneBm25Reader, "bm25", "shared BM25 index");
    }

    private static void BuildLuceneSimilarityIndex(
        string indexPath,
        string modelId,
        Lucene.Net.Search.Similarities.Similarity similarity)
    {
        IODirectory.CreateDirectory(indexPath);
        using var directory = new LuceneMMapDirectory(new DirectoryInfo(indexPath));
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        var writerConfig = new Lucene.Net.Index.IndexWriterConfig(
            LuceneVersion.LUCENE_48, analyzer)
        {
            Similarity = similarity
        };
        ValidateBval08SimilarityProfile(writerConfig.Similarity, modelId, "index writer");

        using (var writer = new Lucene.Net.Index.IndexWriter(directory, writerConfig))
        {
            writer.SetCommitData(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Bval05SimilarityCommitDataKey] = Bval08CommittedProfile(modelId)
            });

            for (int i = 0; i < _documents.Length; i++)
            {
                var document = new Lucene.Net.Documents.Document
                {
                    new LuceneStringField("id",
                        i.ToString(CultureInfo.InvariantCulture),
                        Lucene.Net.Documents.Field.Store.NO),
                    new LuceneTextField("body", _documents[i],
                        Lucene.Net.Documents.Field.Store.NO)
                };
                writer.AddDocument(document);
            }

            writer.Commit();
        }

        using var reader = DirectoryReader.Open(directory);
        ValidateBval08IndexProfile(reader, modelId, $"shared {modelId} index");
    }

    internal static Lucene.Net.Search.Similarities.BM25Similarity CreateBval05Bm25Similarity()
        => new(1.2f, 0.75f);

    private static void ValidateBval05Bm25Reader(DirectoryReader reader, string stage)
    {
        if (!reader.IndexCommit.UserData.TryGetValue(Bval05SimilarityCommitDataKey, out string? profile)
            || !string.Equals(profile, Bval05SimilarityProfile, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"BVAL-05 Lucene {stage} does not carry the expected committed index-time profile.");
        }

        Lucene.Net.Index.FieldInfo? bodyField = Lucene.Net.Index.MultiFields
            .GetMergedFieldInfos(reader)
            .FieldInfo("body");
        if (bodyField is null || !bodyField.HasNorms)
            throw new InvalidOperationException($"BVAL-05 Lucene {stage} must contain body norms.");
    }

    private static void ValidateBval05Fixture()
    {
        string fixturePath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            Bval05FixturePath.Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllBytes(fixturePath));
        JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(),
                "bval-05-matched-bm25-index-and-query", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
            throw new InvalidOperationException($"BVAL-05 fixture metadata is invalid: {Bval05FixturePath}.");

        JsonElement input = root.GetProperty("input");
        JsonElement query = input.GetProperty("query");
        JsonElement similarity = input.GetProperty("similarity");
        var documents = input.GetProperty("documents").EnumerateArray()
            .Select(static item => (Id: item.GetProperty("id").GetString()!, Body: item.GetProperty("body").GetString()!))
            .ToArray();
        JsonElement expected = root.GetProperty("expected");
        string[] expectedIds = expected.GetProperty("logicalHitIds").EnumerateArray()
            .Select(static item => item.GetString()!)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        string matchedComparisonGroup = expected.GetProperty("matchedComparisonGroupId").GetString()!;
        string asShippedComparisonGroup = expected.GetProperty("asShippedComparisonGroupId").GetString()!;

        if (!string.Equals(input.GetProperty("analyser").GetString(), "standard-lowercase-v1", StringComparison.Ordinal)
            || !string.Equals(query.GetProperty("field").GetString(), "body", StringComparison.Ordinal)
            || !string.Equals(similarity.GetProperty("name").GetString(), "BM25", StringComparison.Ordinal)
            || Math.Abs(similarity.GetProperty("k1").GetSingle() - 1.2f) > 1e-6f
            || Math.Abs(similarity.GetProperty("b").GetSingle() - 0.75f) > 1e-6f
            || !string.Equals(expected.GetProperty("indexTimeSimilarity").GetString(), Bval05SimilarityProfile, StringComparison.Ordinal)
            || !string.Equals(expected.GetProperty("queryTimeSimilarity").GetString(), Bval05SimilarityProfile, StringComparison.Ordinal)
            || !expected.GetProperty("normsMustBeCreatedForTheSelectedIndexSimilarity").GetBoolean()
            || !expected.GetProperty("classicTfIdfDefaultMustUseASeparateAsShippedComparisonId").GetBoolean()
            || string.IsNullOrWhiteSpace(matchedComparisonGroup)
            || string.IsNullOrWhiteSpace(asShippedComparisonGroup)
            || string.Equals(matchedComparisonGroup, asShippedComparisonGroup, StringComparison.Ordinal)
            || documents.Length == 0
            || documents.Select(static item => item.Id).Distinct(StringComparer.Ordinal).Count() != documents.Length
            || !expectedIds.SequenceEqual(documents.Select(static item => item.Id).OrderBy(static id => id, StringComparer.Ordinal), StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"BVAL-05 fixture does not define a valid matched BM25 profile: {Bval05FixturePath}.");
        }

        ValidateBval05ComparisonGroups(matchedComparisonGroup, asShippedComparisonGroup);
        ValidateBval05Bm25Profile(_luceneBm25Searcher?.Similarity
            ?? throw new InvalidOperationException("BVAL-05 shared Lucene BM25 searcher is unavailable."), "shared query searcher");
        ValidateBval05Bm25Reader(_luceneBm25Reader
            ?? throw new InvalidOperationException("BVAL-05 shared Lucene BM25 reader is unavailable."), "shared BM25 index");
        if (_luceneSearcher?.Similarity is not Lucene.Net.Search.Similarities.DefaultSimilarity)
            throw new InvalidOperationException("BVAL-05 Lucene as-shipped searcher must retain the default Classic TF-IDF profile.");

        var sharedLeanResults = (_leanSearcher
            ?? throw new InvalidOperationException("BVAL-05 shared LeanCorpus searcher is unavailable."))
            .Search(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "government"), _docCount);
        var sharedLuceneResults = _luceneBm25Searcher.Search(
            new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government")), _docCount);
        int[] sharedLeanIds = sharedLeanResults.ScoreDocs.Select(static hit => hit.DocId).Order().ToArray();
        int[] sharedLuceneIds = sharedLuceneResults.ScoreDocs.Select(static hit => hit.Doc).Order().ToArray();
        if (sharedLeanResults.TotalHits != sharedLuceneResults.TotalHits
            || !sharedLeanIds.SequenceEqual(sharedLuceneIds))
        {
            throw new InvalidOperationException("BVAL-05 shared LeanCorpus and matched Lucene BM25 paths returned different logical hit IDs.");
        }

        string leanPath = BenchmarkHelpers.CreateTempDirectory("lc-bval-05-fixture-lean");
        string lucenePath = BenchmarkHelpers.CreateTempDirectory("lc-bval-05-fixture-lucene");
        try
        {
            using var leanDirectory = new LeanMMapDirectory(leanPath);
            using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
                leanDirectory, new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig()))
            {
                foreach ((string id, string body) in documents)
                {
                    var document = new LeanDocument();
                    document.Add(new LeanStringField("id", id));
                    document.Add(new LeanTextField("body", body));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }

            using var leanSearcher = new LeanIndexSearcher(leanDirectory);
            var leanFixtureResults = leanSearcher.Search(
                new Rowles.LeanCorpus.Search.Queries.TermQuery("body", query.GetProperty("term").GetString()!), documents.Length);
            string[] leanFixtureIds = leanFixtureResults.ScoreDocs
                .Select(hit => leanSearcher.GetStoredFields(hit.DocId)["id"][0])
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();

            using var luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(lucenePath));
            using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
            var writerConfig = new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)
            {
                Similarity = CreateBval05Bm25Similarity()
            };
            ValidateBval05Bm25Profile(writerConfig.Similarity, "fixture index writer");
            using (var writer = new Lucene.Net.Index.IndexWriter(luceneDirectory, writerConfig))
            {
                writer.SetCommitData(new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Bval05SimilarityCommitDataKey] = Bval05SimilarityProfile
                });
                foreach ((string id, string body) in documents)
                {
                    var document = new Lucene.Net.Documents.Document
                    {
                        new LuceneStringField("id", id, Lucene.Net.Documents.Field.Store.YES),
                        new LuceneTextField("body", body, Lucene.Net.Documents.Field.Store.NO)
                    };
                    writer.AddDocument(document);
                }
                writer.Commit();
            }

            using var luceneReader = DirectoryReader.Open(luceneDirectory);
            ValidateBval05Bm25Reader(luceneReader, "fixture BM25 index");
            var luceneSearcher = new LuceneIndexSearcher(luceneReader)
            {
                Similarity = CreateBval05Bm25Similarity()
            };
            ValidateBval05Bm25Profile(luceneSearcher.Similarity, "fixture query searcher");
            var luceneFixtureResults = luceneSearcher.Search(
                new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", query.GetProperty("term").GetString()!)),
                documents.Length);
            string[] luceneFixtureIds = luceneFixtureResults.ScoreDocs
                .Select(hit => luceneReader.Document(hit.Doc).Get("id")!)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();

            if (leanFixtureResults.TotalHits != expectedIds.Length
                || luceneFixtureResults.TotalHits != expectedIds.Length
                || !expectedIds.SequenceEqual(leanFixtureIds, StringComparer.Ordinal)
                || !expectedIds.SequenceEqual(luceneFixtureIds, StringComparer.Ordinal)
                || !leanFixtureIds.SequenceEqual(luceneFixtureIds, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"BVAL-05 matched BM25 fixture hits differ. Expected=[{string.Join(",", expectedIds)}]; " +
                    $"LeanCorpus=[{string.Join(",", leanFixtureIds)}]; Lucene.NET=[{string.Join(",", luceneFixtureIds)}].");
            }
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(leanPath);
            BenchmarkHelpers.DeleteDirectory(lucenePath);
        }
    }

    private static void ValidateBval05ComparisonGroups(string matchedGroup, string asShippedGroup)
    {
        string manifestPath = Path.Combine(BenchmarkHelpers.RepositoryRoot,
            ".config/devops/benchmark-cases.json".Replace('/', Path.DirectorySeparatorChar));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        JsonElement families = manifest.RootElement.GetProperty("caseFamilies");
        string[] matchedMethods = families.EnumerateArray()
            .Where(item => string.Equals(item.GetProperty("comparisonGroup").GetString(), matchedGroup, StringComparison.Ordinal))
            .SelectMany(static item => item.GetProperty("benchmarkMethods").EnumerateArray())
            .Select(static method => method.GetString()!)
            .ToArray();
        if (!matchedMethods.Contains("LeanCorpus_Bm25_TermQuery", StringComparer.Ordinal)
            || !matchedMethods.Contains("LuceneNet_Bm25_TermQuery", StringComparer.Ordinal))
            throw new InvalidOperationException("BVAL-05 matched BM25 comparison group must include both benchmark engines.");

        string[] asShippedMethods = families.EnumerateArray()
            .Where(item => string.Equals(item.GetProperty("comparisonGroup").GetString(), asShippedGroup, StringComparison.Ordinal))
            .SelectMany(static item => item.GetProperty("benchmarkMethods").EnumerateArray())
            .Select(static method => method.GetString()!)
            .ToArray();
        if (!asShippedMethods.Contains("LuceneNet_TfIdf_TermQuery", StringComparer.Ordinal)
            || matchedMethods.Contains("LuceneNet_TfIdf_TermQuery", StringComparer.Ordinal))
            throw new InvalidOperationException("BVAL-05 as-shipped Classic TF-IDF must use its own comparison group.");
    }

    /// <summary>Documents used to build the shared index.</summary>
    public static string[] Documents
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            if (_documents.Length == 0)
                _documents = BenchmarkData.BuildDocuments(_docCount);
            return _documents;
        }
    }

    /// <summary>Path to the shared LeanCorpus index directory.</summary>
    public static string LeanIndexPath
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _leanIndexPath;
        }
    }

    /// <summary>Pre-built IndexSearcher for the shared LeanCorpus index.</summary>
    public static LeanIndexSearcher LeanSearcher
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _leanSearcher!;
        }
    }

    /// <summary>Pre-built BM25 IndexSearcher with BM25 index-time norms.</summary>
    public static LuceneIndexSearcher LuceneSearcher
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _luceneBm25Searcher!;
        }
    }

    /// <summary>Pre-built Lucene.NET default-similarity searcher, retained as shipped.</summary>
    public static LuceneIndexSearcher LuceneAsShippedSearcher
    {
        get
        {
            EnsureInitialised(ResolveDocCount());
            return _luceneSearcher!;
        }
    }

    /// <summary>
    /// Creates a fresh <see cref="LeanIndexSearcher"/> on the shared directory.
    /// Use when the suite needs its own searcher lifecycle (e.g. SearcherManager
    /// or QueryCache suites).
    /// </summary>
    public static LeanIndexSearcher CreateSearcher()
    {
        EnsureInitialised(ResolveDocCount());
        return new LeanIndexSearcher(new LeanMMapDirectory(_leanIndexPath));
    }

    /// <summary>
    /// Creates a fresh <see cref="LeanMMapDirectory"/> pointing at the shared
    /// index path. Callers own the instance and must dispose it.
    /// </summary>
    public static LeanMMapDirectory CreateDirectory()
    {
        EnsureInitialised(ResolveDocCount());
        return new LeanMMapDirectory(_leanIndexPath);
    }

    /// <summary>
    /// Creates a fresh Lucene.NET directory pointing at the matched BM25 index.
    /// The caller owns the returned directory.
    /// </summary>
    public static LuceneMMapDirectory CreateLuceneDirectory()
    {
        EnsureInitialised(ResolveDocCount());
        return new LuceneMMapDirectory(new DirectoryInfo(_luceneBm25IndexPath));
    }

    /// <summary>Creates a fresh directory for Lucene.NET's default as-shipped similarity profile.</summary>
    public static LuceneMMapDirectory CreateLuceneAsShippedDirectory()
    {
        EnsureInitialised(ResolveDocCount());
        return new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
    }

    /// <summary>Creates a fresh directory for the model-specific Dirichlet index.</summary>
    public static LuceneMMapDirectory CreateLuceneDirichletDirectory()
    {
        EnsureInitialised(ResolveDocCount());
        EnsureLuceneSimilarityIndexes();
        return new LuceneMMapDirectory(new DirectoryInfo(_luceneDirichletIndexPath));
    }

    /// <summary>Creates a fresh directory for the model-specific Jelinek-Mercer index.</summary>
    public static LuceneMMapDirectory CreateLuceneJelinekMercerDirectory()
    {
        EnsureInitialised(ResolveDocCount());
        EnsureLuceneSimilarityIndexes();
        return new LuceneMMapDirectory(new DirectoryInfo(_luceneJelinekMercerIndexPath));
    }

    /// <summary>Delete model-specific Lucene indexes created by the current process.</summary>
    public static void CleanupOwnedLuceneSimilarityIndexes()
    {
        lock (Gate)
        {
            if (!_ownsSimilarityIndexDirectories)
                return;

            BenchmarkHelpers.DeleteDirectory(_luceneDirichletIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneJelinekMercerIndexPath);
            _luceneDirichletIndexPath = string.Empty;
            _luceneJelinekMercerIndexPath = string.Empty;
            _ownsSimilarityIndexDirectories = false;
        }
    }

    private static void EnsureLuceneSimilarityIndexes()
    {
        lock (Gate)
        {
            bool hasDirichlet = !string.IsNullOrWhiteSpace(_luceneDirichletIndexPath)
                && Directory.Exists(_luceneDirichletIndexPath);
            bool hasJelinekMercer = !string.IsNullOrWhiteSpace(_luceneJelinekMercerIndexPath)
                && Directory.Exists(_luceneJelinekMercerIndexPath);
            if (hasDirichlet && hasJelinekMercer)
                return;
            if (hasDirichlet != hasJelinekMercer)
                throw new InvalidOperationException("BVAL-08 requires both model-specific Lucene language-model indexes.");

            if (_documents.Length != _docCount)
                _documents = BenchmarkData.BuildDocuments(_docCount);

            _luceneDirichletIndexPath = Path.Combine(
                BenchmarkHelpers.TempRoot,
                $"lucenenet-shared-dirichlet-stdidx-{Guid.NewGuid():N}");
            _luceneJelinekMercerIndexPath = Path.Combine(
                BenchmarkHelpers.TempRoot,
                $"lucenenet-shared-jelinek-mercer-stdidx-{Guid.NewGuid():N}");
            try
            {
                BuildLuceneSimilarityIndex(
                    _luceneDirichletIndexPath,
                    "dirichlet",
                    CreateBval08Similarity("dirichlet"));
                BuildLuceneSimilarityIndex(
                    _luceneJelinekMercerIndexPath,
                    "jelinek-mercer",
                    CreateBval08Similarity("jelinek-mercer"));
                _ownsSimilarityIndexDirectories = true;
            }
            catch
            {
                BenchmarkHelpers.DeleteDirectory(_luceneDirichletIndexPath);
                BenchmarkHelpers.DeleteDirectory(_luceneJelinekMercerIndexPath);
                _luceneDirichletIndexPath = string.Empty;
                _luceneJelinekMercerIndexPath = string.Empty;
                throw;
            }
        }
    }

    /// <summary>
    /// Release all shared resources and delete temp index directories.
    /// Called at the end of the benchmark run.
    /// </summary>
    public static void Cleanup()
    {
        lock (Gate)
        {
            Teardown();
        }
    }

    private static void Teardown()
    {
        // LeanCorpus
        _leanSearcher?.Dispose();
        _leanSearcher = null;
        _leanDirectory?.Dispose();
        _leanDirectory = null;
        if (_ownsIndexDirectories)
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        _leanIndexPath = string.Empty;

        // Lucene.NET
        _luceneSearcher = null;
        _luceneReader?.Dispose();
        _luceneReader = null;
        _luceneAnalyzer?.Dispose();
        _luceneAnalyzer = null;
        _luceneDirectory?.Dispose();
        _luceneDirectory = null;
        if (_ownsIndexDirectories)
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
        _luceneIndexPath = string.Empty;

        _luceneBm25Searcher = null;
        _luceneBm25Reader?.Dispose();
        _luceneBm25Reader = null;
        _luceneBm25Analyzer?.Dispose();
        _luceneBm25Analyzer = null;
        _luceneBm25Directory?.Dispose();
        _luceneBm25Directory = null;
        if (_ownsIndexDirectories)
            BenchmarkHelpers.DeleteDirectory(_luceneBm25IndexPath);
        _luceneBm25IndexPath = string.Empty;

        if (_ownsIndexDirectories || _ownsSimilarityIndexDirectories)
        {
            BenchmarkHelpers.DeleteDirectory(_luceneDirichletIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneJelinekMercerIndexPath);
        }
        _luceneDirichletIndexPath = string.Empty;
        _luceneJelinekMercerIndexPath = string.Empty;
        _ownsSimilarityIndexDirectories = false;

        _documents = [];
        _docCount = 0;
        _ownsIndexDirectories = false;
        _initialised = false;
    }

    private static int ResolveDocCount()
    {
        var env = Environment.GetEnvironmentVariable("BENCH_DOC_COUNT");
        if (int.TryParse(env, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var n) && n > 0)
            return n;
        return BenchmarkData.DefaultDocCount;
    }

    internal static void ValidateBval05Bm25Profile(
        Lucene.Net.Search.Similarities.Similarity similarity,
        string stage)
    {
        if (similarity is not Lucene.Net.Search.Similarities.BM25Similarity bm25
            || Math.Abs(bm25.K1 - 1.2f) > 1e-6f
            || Math.Abs(bm25.B - 0.75f) > 1e-6f)
            throw new InvalidOperationException(
                $"BVAL-05 Lucene {stage} must use BM25(k1=1.2,b=0.75).");
    }

    internal static Lucene.Net.Search.Similarities.Similarity CreateBval08Similarity(string modelId)
        => modelId switch
        {
            "bm25" => CreateBval05Bm25Similarity(),
            "classic-tfidf" => new Lucene.Net.Search.Similarities.DefaultSimilarity(),
            "dirichlet" => new Lucene.Net.Search.Similarities.LMDirichletSimilarity(2000f),
            "jelinek-mercer" => new Lucene.Net.Search.Similarities.LMJelinekMercerSimilarity(0.1f),
            _ => throw new ArgumentOutOfRangeException(nameof(modelId), modelId, "Unknown BVAL-08 similarity model.")
        };

    internal static void ValidateBval08SimilarityProfile(
        Lucene.Net.Search.Similarities.Similarity similarity,
        string modelId,
        string stage)
    {
        bool matches = modelId switch
        {
            "bm25" => similarity is Lucene.Net.Search.Similarities.BM25Similarity bm25
                && Math.Abs(bm25.K1 - 1.2f) <= 1e-6f
                && Math.Abs(bm25.B - 0.75f) <= 1e-6f,
            "classic-tfidf" => similarity is Lucene.Net.Search.Similarities.DefaultSimilarity,
            "dirichlet" => similarity is Lucene.Net.Search.Similarities.LMDirichletSimilarity dirichlet
                && Math.Abs(dirichlet.Mu - 2000f) <= 1e-6f,
            "jelinek-mercer" => similarity is Lucene.Net.Search.Similarities.LMJelinekMercerSimilarity jelinekMercer
                && Math.Abs(jelinekMercer.Lambda - 0.1f) <= 1e-6f,
            _ => false
        };

        if (!matches)
            throw new InvalidOperationException(
                $"BVAL-08 Lucene {stage} does not use the configured {modelId} similarity profile.");
    }

    internal static void ValidateBval08IndexProfile(
        DirectoryReader reader,
        string modelId,
        string stage)
    {
        string expectedProfile = Bval08CommittedProfile(modelId);
        if (!reader.IndexCommit.UserData.TryGetValue(Bval05SimilarityCommitDataKey, out string? actualProfile)
            || !string.Equals(actualProfile, expectedProfile, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"BVAL-08 Lucene {stage} does not carry the expected committed {modelId} index-time profile.");
        }

        Lucene.Net.Index.FieldInfo? bodyField = Lucene.Net.Index.MultiFields
            .GetMergedFieldInfos(reader)
            .FieldInfo("body");
        if (bodyField is null || !bodyField.HasNorms)
            throw new InvalidOperationException($"BVAL-08 Lucene {stage} must contain body norms.");
    }

    private static string Bval08CommittedProfile(string modelId)
        => modelId switch
        {
            "bm25" => Bval05SimilarityProfile,
            "classic-tfidf" => Bval08ClassicTfIdfProfile,
            "dirichlet" => Bval08DirichletProfile,
            "jelinek-mercer" => Bval08JelinekMercerProfile,
            _ => throw new ArgumentOutOfRangeException(nameof(modelId), modelId, "Unknown BVAL-08 similarity model.")
        };
}
