using BenchmarkDotNet.Attributes;
using System.Security.Cryptography;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search.Spell;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Suggestions;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares DidYouMean suggestion speed against Lucene.NET SpellChecker.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
public class SuggesterBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _timedQueries = [];

    // LeanCorpus state
    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private SpellIndex? _leanSpellIndex;

    // Lucene.NET state
    private RAMDirectory? _luceneSourceDirectory;
    private RAMDirectory? _luceneSpellDirectory;
    private SpellChecker? _luceneSpellChecker;

    [GlobalSetup]
    public void Setup()
    {
        SuggesterQualityFixture fixture = SuggesterQualityFixture.Load(BenchmarkHelpers.RepositoryRoot);
        fixture.ValidateAgainstDataForgeWorkload(BenchmarkData.BuildMisspelledTerms());
        _timedQueries = fixture.CorrectionCases
            .Select(static item => SuggesterQualityNormalisation.Normalise(item.Query))
            .Append(SuggesterQualityNormalisation.Normalise(fixture.NoSuggestionCase.Query))
            .ToArray();
        var documents = BenchmarkData.BuildDocuments(DocumentCount);

        BuildLeanCorpusIndex(documents);
        _leanSpellIndex = SpellIndex.Build(_leanSearcher!, "body");
        BuildLuceneNetSpellChecker(documents);
        SuggesterQualityEvidenceCollector.WriteForCurrentRun(BuildQualityEvidence(fixture));
    }

    internal SuggesterQualityEvidence BuildQualityEvidence()
        => BuildQualityEvidence(SuggesterQualityFixture.Load(BenchmarkHelpers.RepositoryRoot));

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && System.IO.Directory.Exists(_leanIndexPath))
            System.IO.Directory.Delete(_leanIndexPath, recursive: true);

        _luceneSpellChecker?.Dispose();
        _luceneSourceDirectory?.Dispose();
        _luceneSpellDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_DidYouMean()
    {
        int total = 0;
        foreach (string query in _timedQueries)
        {
            var suggestions = DidYouMeanSuggester.Suggest(_leanSearcher!, "body", query, maxEdits: 2, topN: 5);
            total += suggestions.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_SpellIndex()
    {
        int total = 0;
        foreach (string query in _timedQueries)
        {
            var suggestions = _leanSpellIndex!.Suggest(query, maxEdits: 2, topN: 5);
            total += suggestions.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SpellChecker()
    {
        int total = 0;
        foreach (string query in _timedQueries)
        {
            var suggestions = _luceneSpellChecker!.SuggestSimilar(query, 5);
            total += suggestions.Length;
        }
        return total;
    }

    private SuggesterQualityEvidence BuildQualityEvidence(SuggesterQualityFixture fixture)
    {
        if (_leanSearcher is null || _leanSpellIndex is null || _luceneSpellChecker is null || _luceneSourceDirectory is null)
            throw new InvalidOperationException("The suggester indexes must be built before quality evidence is collected.");

        fixture.ValidateAgainstDataForgeWorkload(BenchmarkData.BuildMisspelledTerms());
        using var luceneReader = DirectoryReader.Open(_luceneSourceDirectory);
        ValidateGroundTruthVocabulary(fixture, luceneReader);

        var engines = new[]
        {
            BuildEngineEvidence("DidYouMean", nameof(LeanCorpus_DidYouMean), fixture,
                query => DidYouMeanSuggester.Suggest(_leanSearcher, "body", query, fixture.MaximumEditDistance, fixture.TopN)
                    .Select(static suggestion => suggestion.Term).ToArray()),
            BuildEngineEvidence("SpellIndex", nameof(LeanCorpus_SpellIndex), fixture,
                query => _leanSpellIndex.Suggest(query, fixture.MaximumEditDistance, fixture.TopN)
                    .Select(static suggestion => suggestion.Term).ToArray()),
            BuildEngineEvidence("LuceneSpellChecker", nameof(LuceneNet_SpellChecker), fixture,
                query => _luceneSpellChecker.SuggestSimilar(query, fixture.TopN))
        };

        return new SuggesterQualityEvidence
        {
            FixtureId = fixture.FixtureId,
            FixtureSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(
                BenchmarkHelpers.RepositoryRoot,
                ".config", "devops", "benchmark-correctness-fixtures", "bval-15-suggester-quality-ground-truth.json")))).ToLowerInvariant(),
            DocumentCount = DocumentCount,
            MaximumEditDistance = fixture.MaximumEditDistance,
            TopN = fixture.TopN,
            QueryNormalisation = fixture.QueryNormalisation,
            Engines = engines
        };
    }

    private static SuggesterEngineQualityEvidence BuildEngineEvidence(
        string engineId,
        string benchmarkMethod,
        SuggesterQualityFixture fixture,
        Func<string, string[]> suggest)
    {
        var timedCases = fixture.CorrectionCases
            .Select(item => (
                item.Id,
                Query: SuggesterQualityNormalisation.Normalise(item.Query),
                item.AcceptableCorrections,
                IsNoSuggestion: false,
                ExpectNoSuggestion: false))
            .Append((
                fixture.NoSuggestionCase.Id,
                Query: SuggesterQualityNormalisation.Normalise(fixture.NoSuggestionCase.Query),
                fixture.NoSuggestionCase.AcceptableCorrections,
                IsNoSuggestion: true,
                ExpectNoSuggestion: fixture.NoSuggestionCase.ExpectNoSuggestion))
            .ToArray();
        SuggesterQualityQueryResult[] queryResults = timedCases
            .Select(item => BuildQueryResult(item.Id, item.Query, item.AcceptableCorrections,
                item.IsNoSuggestion, item.ExpectNoSuggestion, suggest(item.Query), fixture.TopN))
            .ToArray();
        SuggesterQualityQueryResult[] probeResults = fixture.Probes
            .Select(item => BuildQueryResult(item.Id, item.Query, item.AcceptableCorrections,
                isNoSuggestionCase: false, expectNoSuggestion: false,
                suggest(item.NormaliseBeforeSuggesting ? SuggesterQualityNormalisation.Normalise(item.Query) : item.Query),
                fixture.TopN))
            .ToArray();
        SuggesterQualityMetrics metrics = SuggesterQualityScorer.ComputeMetrics(queryResults);

        return new SuggesterEngineQualityEvidence
        {
            EngineId = engineId,
            BenchmarkMethod = benchmarkMethod,
            CorrectionQueryCount = fixture.CorrectionCases.Length,
            TimedQueryCount = queryResults.Length,
            CandidateCount = queryResults.Sum(static result => result.Suggestions.Length),
            Metrics = metrics,
            QueryResults = queryResults,
            ProbeResults = probeResults
        };
    }

    private static SuggesterQualityQueryResult BuildQueryResult(
        string id,
        string query,
        string[] acceptableCorrections,
        bool isNoSuggestionCase,
        bool expectNoSuggestion,
        string[] suggestions,
        int topN)
    {
        string normalisedQuery = SuggesterQualityNormalisation.Normalise(query);
        string[] normalisedCorrections = acceptableCorrections
            .Select(SuggesterQualityNormalisation.Normalise)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var candidates = suggestions
            .Take(topN)
            .Select((term, index) =>
            {
                string normalisedTerm = SuggesterQualityNormalisation.Normalise(term);
                return new SuggesterQualityCandidate
                {
                    Rank = index + 1,
                    Term = term,
                    NormalisedTerm = normalisedTerm,
                    EditDistance = LevenshteinDistance.Compute(normalisedQuery.AsSpan(), normalisedTerm.AsSpan()),
                    IsAcceptableCorrection = normalisedCorrections.Contains(normalisedTerm, StringComparer.Ordinal)
                };
            })
            .ToArray();
        int? firstRelevantRank = candidates
            .Where(static candidate => candidate.IsAcceptableCorrection)
            .Select(static candidate => (int?)candidate.Rank)
            .FirstOrDefault();

        return new SuggesterQualityQueryResult
        {
            Id = id,
            Query = normalisedQuery,
            AcceptableCorrections = normalisedCorrections,
            IsNoSuggestionCase = isNoSuggestionCase,
            ExpectNoSuggestion = expectNoSuggestion,
            FirstRelevantRank = firstRelevantRank,
            NoSuggestionSatisfied = expectNoSuggestion ? candidates.Length == 0 : null,
            Suggestions = candidates
        };
    }

    private void ValidateGroundTruthVocabulary(SuggesterQualityFixture fixture, DirectoryReader luceneReader)
    {
        var cases = fixture.CorrectionCases
            .SelectMany(item => item.AcceptableCorrections)
            .Concat(fixture.Probes.SelectMany(static item => item.AcceptableCorrections))
            .Distinct(StringComparer.Ordinal);
        foreach (string correction in cases)
        {
            string term = SuggesterQualityNormalisation.Normalise(correction);
            if (_leanSearcher!.Count(new TermQuery("body", term)) == 0 ||
                luceneReader.DocFreq(new Lucene.Net.Index.Term("body", term)) == 0)
                throw new InvalidDataException($"BVAL-15 correction '{correction}' is absent from one or both benchmark vocabularies.");
            foreach (SuggesterCorrectionCase item in fixture.CorrectionCases.Where(item =>
                         item.AcceptableCorrections.Contains(correction, StringComparer.Ordinal)))
            {
                string query = SuggesterQualityNormalisation.Normalise(item.Query);
                if (LevenshteinDistance.Compute(query.AsSpan(), term.AsSpan()) > fixture.MaximumEditDistance)
                    throw new InvalidDataException($"BVAL-15 correction '{correction}' exceeds the pinned edit-distance limit for query '{item.Query}'.");
            }
        }
    }

    private void BuildLeanCorpusIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-suggest-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_leanIndexPath);

        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using (var writer = new LeanIndexWriter(_leanDirectory, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        }))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", documents[i]));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private void BuildLuceneNetSpellChecker(string[] documents)
    {
        _luceneSourceDirectory = new RAMDirectory();
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneSourceDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var doc = new Lucene.Net.Documents.Document
                {
                    new LuceneTextField("body", documents[i], Field.Store.NO)
                };
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _luceneSpellDirectory = new RAMDirectory();
        _luceneSpellChecker = new SpellChecker(_luceneSpellDirectory);

        using var reader = DirectoryReader.Open(_luceneSourceDirectory);
        _luceneSpellChecker.IndexDictionary(
            new LuceneDictionary(reader, "body"),
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer),
            fullMerge: true);
    }
}
