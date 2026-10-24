using BenchmarkDotNet.Attributes;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares search latency across multiple <see cref="ISimilarity"/> scoring models
/// (BM25, TF-IDF, language models, and advanced variants) on the same query and
/// logically identical documents. Each Lucene.NET scorer uses an index written
/// with its own similarity profile. Lucene.NET methods cover BM25, TF-IDF,
/// Dirichlet and Jelinek-Mercer.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class SimilarityBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    // LeanCorpus state
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _bm25Searcher;
    private LeanIndexSearcher? _tfIdfSearcher;
    private LeanIndexSearcher? _bm25PlusSearcher;
    private LeanIndexSearcher? _bm25LSearcher;
    private LeanIndexSearcher? _tfIdfAugmentedSearcher;
    private LeanIndexSearcher? _tfIdfPivotedSearcher;
    private LeanIndexSearcher? _tfIdfDoubleNormSearcher;
    private LeanIndexSearcher? _dirichletSearcher;
    private LeanIndexSearcher? _jmSearcher;
    private LeanIndexSearcher? _absDiscountingSearcher;

    // Lucene.NET state
    private LuceneMMapDirectory? _luceneDirectory;
    private Lucene.Net.Index.DirectoryReader? _luceneReader;
    private LuceneMMapDirectory? _luceneBm25Directory;
    private Lucene.Net.Index.DirectoryReader? _luceneBm25Reader;
    private LuceneMMapDirectory? _luceneDirichletDirectory;
    private Lucene.Net.Index.DirectoryReader? _luceneDirichletReader;
    private LuceneMMapDirectory? _luceneJelinekMercerDirectory;
    private Lucene.Net.Index.DirectoryReader? _luceneJelinekMercerReader;
    private LuceneIndexSearcher? _luceneBm25Searcher;
    private LuceneIndexSearcher? _luceneTfIdfSearcher;
    private LuceneIndexSearcher? _luceneDirichletSearcher;
    private LuceneIndexSearcher? _luceneJMSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        ValidateBval08Fixture();
        try
        {
            _leanDirectory = SharedStandardIndex.CreateDirectory();
            CreateLeanSearchers(_leanDirectory);
            _luceneDirectory = SharedStandardIndex.CreateLuceneAsShippedDirectory();
            _luceneBm25Directory = SharedStandardIndex.CreateLuceneDirectory();
            _luceneDirichletDirectory = SharedStandardIndex.CreateLuceneDirichletDirectory();
            _luceneJelinekMercerDirectory = SharedStandardIndex.CreateLuceneJelinekMercerDirectory();
            CreateLuceneSearchers(
                _luceneDirectory,
                _luceneBm25Directory,
                _luceneDirichletDirectory,
                _luceneJelinekMercerDirectory);
        }
        catch
        {
            CleanupSearchers();
            _leanDirectory?.Dispose();
            _luceneReader?.Dispose();
            _luceneDirectory?.Dispose();
            _luceneBm25Reader?.Dispose();
            _luceneBm25Directory?.Dispose();
            _luceneDirichletReader?.Dispose();
            _luceneDirichletDirectory?.Dispose();
            _luceneJelinekMercerReader?.Dispose();
            _luceneJelinekMercerDirectory?.Dispose();
            SharedStandardIndex.CleanupOwnedLuceneSimilarityIndexes();
            throw;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        CleanupSearchers();
        _leanDirectory?.Dispose();

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        _luceneBm25Reader?.Dispose();
        _luceneBm25Directory?.Dispose();
        _luceneDirichletReader?.Dispose();
        _luceneDirichletDirectory?.Dispose();
        _luceneJelinekMercerReader?.Dispose();
        _luceneJelinekMercerDirectory?.Dispose();
        SharedStandardIndex.CleanupOwnedLuceneSimilarityIndexes();
    }

    private void CleanupSearchers()
    {
        _bm25Searcher?.Dispose();
        _tfIdfSearcher?.Dispose();
        _bm25PlusSearcher?.Dispose();
        _bm25LSearcher?.Dispose();
        _tfIdfAugmentedSearcher?.Dispose();
        _tfIdfPivotedSearcher?.Dispose();
        _tfIdfDoubleNormSearcher?.Dispose();
        _dirichletSearcher?.Dispose();
        _jmSearcher?.Dispose();
        _absDiscountingSearcher?.Dispose();
    }

    // --- Baseline  ---

    [Benchmark(Baseline = true)]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25_TermQuery()
        => _bm25Searcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Bm25_TermQuery()
    {
        var q = new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government"));
        return _luceneBm25Searcher!.Search(q, TopN).TotalHits;
    }

    // --- Classic TF-IDF  ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdf_TermQuery()
        => _tfIdfSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TfIdf_TermQuery()
    {
        var q = new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government"));
        return _luceneTfIdfSearcher!.Search(q, TopN).TotalHits;
    }

    // --- Language model (with Lucene.NET parity) ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Dirichlet_TermQuery()
        => _dirichletSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Dirichlet_TermQuery()
    {
        var q = new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government"));
        return _luceneDirichletSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_JelinekMercer_TermQuery()
        => _jmSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_JelinekMercer_TermQuery()
    {
        var q = new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government"));
        return _luceneJMSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_AbsoluteDiscounting_TermQuery()
        => _absDiscountingSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    // --- Advanced variants (LeanCorpus only) ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25Plus_TermQuery()
        => _bm25PlusSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25L_TermQuery()
        => _bm25LSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdfAugmented_TermQuery()
        => _tfIdfAugmentedSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdfPivoted_TermQuery()
        => _tfIdfPivotedSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdfDoubleNorm_TermQuery()
        => _tfIdfDoubleNormSearcher!.Search(new TermQuery("body", "government"), TopN).TotalHits;

    // --- Boolean query variants ---

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _bm25Searcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("bm25")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Bm25_BooleanQuery()
    {
        var q = new Lucene.Net.Search.BooleanQuery
        {
            { new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "government")), Lucene.Net.Search.Occur.MUST },
            { new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", "market")), Lucene.Net.Search.Occur.SHOULD }
        };
        return _luceneBm25Searcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_TfIdf_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _tfIdfSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("lm")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Dirichlet_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _dirichletSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark]
    [BenchmarkCategory("similarity")]
    [BenchmarkCategory("variant")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Bm25Plus_BooleanQuery()
    {
        var builder = new BooleanQuery.Builder();
        builder.Add(new TermQuery("body", "government"), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery("body", "market"), Rowles.LeanCorpus.Search.Occur.Should);
        return _bm25PlusSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    // --- Index builders ---

    private void CreateLeanSearchers(LeanMMapDirectory directory)
    {
        _bm25Searcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = Bm25Similarity.Instance });

        _tfIdfSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfSimilarity.Instance });

        _bm25PlusSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = Bm25PlusSimilarity.Instance });

        _bm25LSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = Bm25LSimilarity.Instance });

        _tfIdfAugmentedSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfAugmentedSimilarity.Instance });

        _tfIdfPivotedSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfPivotedSimilarity.Instance });

        _tfIdfDoubleNormSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = TfIdfDoubleNormSimilarity.Instance });

        _dirichletSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = DirichletSimilarity.Instance });

        _jmSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = LMJelinekMercerSimilarity.Instance });

        _absDiscountingSearcher = new LeanIndexSearcher(
            directory,
            new IndexSearcherConfig { Similarity = LMAbsoluteDiscountingSimilarity.Instance });
    }

    private void CreateLuceneSearchers(
        LuceneMMapDirectory asShippedDirectory,
        LuceneMMapDirectory bm25Directory,
        LuceneMMapDirectory dirichletDirectory,
        LuceneMMapDirectory jelinekMercerDirectory)
    {
        _luceneReader = Lucene.Net.Index.DirectoryReader.Open(asShippedDirectory);
        _luceneBm25Reader = Lucene.Net.Index.DirectoryReader.Open(bm25Directory);
        _luceneDirichletReader = Lucene.Net.Index.DirectoryReader.Open(dirichletDirectory);
        _luceneJelinekMercerReader = Lucene.Net.Index.DirectoryReader.Open(jelinekMercerDirectory);
        _luceneBm25Searcher = new LuceneIndexSearcher(_luceneBm25Reader)
            { Similarity = SharedStandardIndex.CreateBval08Similarity("bm25") };
        _luceneTfIdfSearcher = new LuceneIndexSearcher(_luceneReader)
            { Similarity = SharedStandardIndex.CreateBval08Similarity("classic-tfidf") };
        _luceneDirichletSearcher = new LuceneIndexSearcher(_luceneDirichletReader)
            { Similarity = SharedStandardIndex.CreateBval08Similarity("dirichlet") };
        _luceneJMSearcher = new LuceneIndexSearcher(_luceneJelinekMercerReader)
            { Similarity = SharedStandardIndex.CreateBval08Similarity("jelinek-mercer") };

        SharedStandardIndex.ValidateBval08SimilarityProfile(_luceneBm25Searcher.Similarity, "bm25", "query searcher");
        SharedStandardIndex.ValidateBval08IndexProfile(_luceneBm25Reader, "bm25", "benchmark BM25 index");
        SharedStandardIndex.ValidateBval08SimilarityProfile(_luceneTfIdfSearcher.Similarity, "classic-tfidf", "query searcher");
        SharedStandardIndex.ValidateBval08IndexProfile(_luceneReader, "classic-tfidf", "benchmark Classic TF-IDF index");
        SharedStandardIndex.ValidateBval08SimilarityProfile(_luceneDirichletSearcher.Similarity, "dirichlet", "query searcher");
        SharedStandardIndex.ValidateBval08IndexProfile(_luceneDirichletReader, "dirichlet", "benchmark Dirichlet index");
        SharedStandardIndex.ValidateBval08SimilarityProfile(_luceneJMSearcher.Similarity, "jelinek-mercer", "query searcher");
        SharedStandardIndex.ValidateBval08IndexProfile(_luceneJelinekMercerReader, "jelinek-mercer", "benchmark Jelinek-Mercer index");
    }

    private static void ValidateBval08Fixture()
    {
        const string fixturePath = ".config/devops/benchmark-correctness-fixtures/bval-08-similarity-specific-norms.json";
        string absolutePath = Path.Combine(
            BenchmarkHelpers.RepositoryRoot,
            fixturePath.Replace('/', Path.DirectorySeparatorChar));
        using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(absolutePath));
        System.Text.Json.JsonElement root = fixture.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1
            || !string.Equals(root.GetProperty("fixtureId").GetString(), "bval-08-similarity-specific-norms", StringComparison.Ordinal)
            || !root.GetProperty("deterministic").GetBoolean())
        {
            throw new InvalidOperationException($"BVAL-08 fixture metadata is invalid: {fixturePath}.");
        }

        System.Text.Json.JsonElement input = root.GetProperty("input");
        System.Text.Json.JsonElement query = input.GetProperty("query");
        var documents = input.GetProperty("documents").EnumerateArray()
            .Select(static item => (Id: item.GetProperty("id").GetString()!, Body: item.GetProperty("body").GetString()!))
            .ToArray();
        string[] expectedLogicalIds = root.GetProperty("expected").GetProperty("logicalHitIds")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        System.Text.Json.JsonElement[] models = input.GetProperty("models").EnumerateArray().ToArray();
        string[] expectedModelIds = ["bm25", "classic-tfidf", "dirichlet", "jelinek-mercer"];
        string[] actualModelIds = models
            .Select(static model => model.GetProperty("id").GetString()!)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        string[] expectedTieIds = root.GetProperty("expected").GetProperty("tieLogicalHitIds")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        float scoreTolerance = root.GetProperty("expected").GetProperty("scoreTolerance").GetSingle();

        if (!string.Equals(query.GetProperty("field").GetString(), "body", StringComparison.Ordinal)
            || !string.Equals(query.GetProperty("term").GetString(), "orchard", StringComparison.Ordinal)
            || documents.Length != expectedLogicalIds.Length
            || documents.Select(static item => item.Id).Distinct(StringComparer.Ordinal).Count() != documents.Length
            || !documents.Select(static item => item.Id).SequenceEqual(expectedLogicalIds, StringComparer.Ordinal)
            || !actualModelIds.SequenceEqual(expectedModelIds.OrderBy(static id => id, StringComparer.Ordinal), StringComparer.Ordinal)
            || expectedTieIds.Length != 2
            || !expectedTieIds.SequenceEqual(expectedLogicalIds.Take(2), StringComparer.Ordinal)
            || !(scoreTolerance > 0f) || !float.IsFinite(scoreTolerance)
            || !root.GetProperty("expected").GetProperty("oneIndexFixturePerIncompatibleNormModel").GetBoolean()
            || !root.GetProperty("expected").GetProperty("writerAndSearcherModelMustMatch").GetBoolean()
            || !root.GetProperty("expected").GetProperty("shortAndLongDocumentNormsMustBeRecordedPerModel").GetBoolean()
            || !root.GetProperty("expected").GetProperty("scoreVectorsMustBeFinite").GetBoolean())
        {
            throw new InvalidOperationException($"BVAL-08 fixture contract is invalid: {fixturePath}.");
        }

        foreach (System.Text.Json.JsonElement model in input.GetProperty("models").EnumerateArray())
        {
            string modelId = model.GetProperty("id").GetString()!;
            string indexProfile = model.GetProperty("indexSimilarity").GetString()!;
            string queryProfile = model.GetProperty("querySimilarity").GetString()!;
            if (!string.Equals(indexProfile, queryProfile, StringComparison.Ordinal))
                throw new InvalidOperationException($"BVAL-08 {modelId} fixture writer and query profiles differ.");

            string tempPath = BenchmarkHelpers.CreateTempDirectory($"lc-bval-08-{modelId}");
            try
            {
                using var directory = new LuceneMMapDirectory(new DirectoryInfo(tempPath));
                using var analyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
                var writerConfig = new Lucene.Net.Index.IndexWriterConfig(
                    Lucene.Net.Util.LuceneVersion.LUCENE_48,
                    analyzer)
                {
                    Similarity = SharedStandardIndex.CreateBval08Similarity(modelId)
                };
                SharedStandardIndex.ValidateBval08SimilarityProfile(writerConfig.Similarity, modelId, "fixture index writer");

                using (var writer = new Lucene.Net.Index.IndexWriter(directory, writerConfig))
                {
                    writer.SetCommitData(new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["leancorpus.benchmark.similarity"] = indexProfile
                    });

                    foreach ((string id, string body) in documents)
                    {
                        var luceneDocument = new Lucene.Net.Documents.Document
                        {
                            new Lucene.Net.Documents.StringField("id", id, Lucene.Net.Documents.Field.Store.YES),
                            new Lucene.Net.Documents.TextField("body", body, Lucene.Net.Documents.Field.Store.NO)
                        };
                        writer.AddDocument(luceneDocument);
                    }

                    writer.Commit();
                }

                using var reader = Lucene.Net.Index.DirectoryReader.Open(directory);
                SharedStandardIndex.ValidateBval08IndexProfile(reader, modelId, "fixture index");
                var searcher = new LuceneIndexSearcher(reader)
                {
                    Similarity = SharedStandardIndex.CreateBval08Similarity(modelId)
                };
                SharedStandardIndex.ValidateBval08SimilarityProfile(searcher.Similarity, modelId, "fixture query searcher");

                var normValues = Lucene.Net.Index.MultiDocValues.GetNormValues(reader, "body")
                    ?? throw new InvalidOperationException($"BVAL-08 {modelId} fixture is missing body norms.");
                var actualNorms = new Dictionary<string, long>(StringComparer.Ordinal);
                for (int docId = 0; docId < reader.MaxDoc; docId++)
                {
                    string logicalId = reader.Document(docId).Get("id")
                        ?? throw new InvalidOperationException($"BVAL-08 {modelId} fixture document {docId} has no logical ID.");
                    actualNorms.Add(logicalId, normValues.Get(docId));
                }

                System.Text.Json.JsonElement expectedNorms = model.GetProperty("expectedNorms");
                if (expectedNorms.EnumerateObject().Count() != actualNorms.Count)
                    throw new InvalidOperationException($"BVAL-08 {modelId} fixture does not record every document norm.");
                foreach (System.Text.Json.JsonProperty expectedNorm in expectedNorms.EnumerateObject())
                {
                    if (!actualNorms.TryGetValue(expectedNorm.Name, out long actualNorm)
                        || actualNorm != expectedNorm.Value.GetInt64())
                    {
                        throw new InvalidOperationException(
                            $"BVAL-08 {modelId} fixture norm for '{expectedNorm.Name}' was {actualNorm}; expected {expectedNorm.Value.GetInt64()}.");
                    }
                }

                var result = searcher.Search(
                    new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term(
                        query.GetProperty("field").GetString()!,
                        query.GetProperty("term").GetString()!)),
                    documents.Length);
                var actualScores = result.ScoreDocs.Select(hit =>
                {
                    string logicalId = reader.Document(hit.Doc).Get("id")
                        ?? throw new InvalidOperationException($"BVAL-08 {modelId} fixture hit {hit.Doc} has no logical ID.");
                    if (!float.IsFinite(hit.Score))
                        throw new InvalidOperationException($"BVAL-08 {modelId} fixture produced a non-finite score.");
                    return (Id: logicalId, Score: hit.Score);
                }).ToArray();

                if (result.TotalHits != documents.Length || actualScores.Length != documents.Length)
                    throw new InvalidOperationException($"BVAL-08 {modelId} fixture returned an incomplete logical hit set.");
                if (!actualScores.Select(static item => item.Id).SequenceEqual(expectedLogicalIds, StringComparer.Ordinal))
                    throw new InvalidOperationException($"BVAL-08 {modelId} fixture logical ranking differs from the canonical order.");
                if (!actualScores.Take(expectedTieIds.Length).Select(static item => item.Id)
                    .SequenceEqual(expectedTieIds, StringComparer.Ordinal)
                    || Math.Abs(actualScores[0].Score - actualScores[1].Score) > scoreTolerance)
                {
                    throw new InvalidOperationException($"BVAL-08 {modelId} fixture tie ranking differs from the canonical order.");
                }

                System.Text.Json.JsonElement expectedScores = model.GetProperty("expectedScores");
                if (expectedScores.GetArrayLength() != actualScores.Length)
                    throw new InvalidOperationException($"BVAL-08 {modelId} fixture returned an unexpected hit count.");
                for (int i = 0; i < actualScores.Length; i++)
                {
                    System.Text.Json.JsonElement expectedScore = expectedScores[i];
                    if (!string.Equals(actualScores[i].Id, expectedScore.GetProperty("id").GetString(), StringComparison.Ordinal)
                        || Math.Abs(actualScores[i].Score - expectedScore.GetProperty("score").GetSingle()) > scoreTolerance)
                    {
                        throw new InvalidOperationException(
                            $"BVAL-08 {modelId} fixture score vector differs at rank {i}: {actualScores[i].Id}={actualScores[i].Score:R}.");
                    }
                }
            }
            finally
            {
                BenchmarkHelpers.DeleteDirectory(tempPath);
            }
        }
    }
}
