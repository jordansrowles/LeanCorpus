using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures the existing vector filter and RRF paths over one DataForge hybrid dataset.</summary>
[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
[WarmupCount(2)]
[IterationCount(5)]
public class HybridSearchBenchmarks
{
    private const int FinalTopN = 10;
    private const int CandidateK = 50;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(20_000);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(64)]
    public int Dimension { get; set; }

    [Params(
        HybridScenario.VectorOnlyAligned,
        HybridScenario.FilteredVectorBroad,
        HybridScenario.FilteredVectorMedium,
        HybridScenario.FilteredVectorNarrow,
        HybridScenario.RrfAligned,
        HybridScenario.RrfConflict)]
    public HybridScenario Scenario { get; set; }

    private string _indexPath = string.Empty;
    private IndexSearcher? _searcher;
    private Query _query = default!;
    private int _searchTopN;
    private int _expectedChecksum;

    [GlobalSetup]
    public void Setup()
    {
        var profile = new LeanCorpusHybridProfile();
        var options = new DataForgeGenerationOptions(42, DocumentCount,
            new Dictionary<string, string> { ["dimension"] = Dimension.ToString(CultureInfo.InvariantCulture) });
        var records = profile.Generate(options).ToArray();
        RegisterIdentity(profile, options, records);
        CheckFilterSelectivity(records);

        _indexPath = BenchmarkHelpers.CreateTempDirectory("lc-hybrid");
        var config = new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = true,
            HnswBuildConfig = new HnswBuildConfig { M = 16, M0 = 32, EfConstruction = 100 },
            HnswSeed = 1L
        };
        using (var writer = new IndexWriter(new MMapDirectory(_indexPath), config))
        {
            foreach (var record in records)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", record.Id));
                document.Add(new TextField("body", record.Body));
                document.Add(new StringField("topic", record.Topic));
                document.Add(new StringField("category", record.Category));
                document.Add(new StringField("accessGroup", record.AccessGroup));
                document.Add(new VectorField("emb", new ReadOnlyMemory<float>(record.Vector)));
                writer.AddDocument(document);
            }
            writer.Commit();
        }
        _searcher = new IndexSearcher(new MMapDirectory(_indexPath));

        var topic = LeanCorpusHybridProfile.Topics[0];
        var queryVector = profile.GenerateTopicQuery(42, 0, Dimension);
        var allVectorRecords = records.Select(static record => new VectorRecord(
            record.Ordinal, record.Id, 0, record.Category, record.AccessGroup, record.Vector)).ToArray();
        var queryCase = new VectorQueryCase("hybrid-topic-0", 0, queryVector);

        PreflightVectorFilters(records, allVectorRecords, queryCase);
        PreflightRrf(records, profile, allVectorRecords, queryCase);

        _query = CreateQuery(Scenario, topic, LeanCorpusHybridProfile.Topics[1], queryVector);
        _searchTopN = Scenario is HybridScenario.RrfAligned or HybridScenario.RrfConflict
            ? CandidateK : FinalTopN;
        var result = _searcher.Search(_query, _searchTopN);
        if (result.ScoreDocs.Length == 0)
            throw new InvalidOperationException($"Hybrid scenario {Scenario} returned no results.");
        _expectedChecksum = Checksum(result.ScoreDocs.Take(FinalTopN).Select(static item => item.DocId));
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_HybridSearch()
    {
        var result = _searcher!.Search(_query, _searchTopN);
        var checksum = Checksum(result.ScoreDocs.Take(FinalTopN).Select(static item => item.DocId));
        if (checksum != _expectedChecksum)
            throw new InvalidOperationException($"Hybrid scenario {Scenario} changed its result ordering.");
        return checksum;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher?.Dispose();
        _searcher = null;
        if (_indexPath.Length != 0)
            BenchmarkHelpers.DeleteDirectory(_indexPath);
        _indexPath = string.Empty;
    }

    private static Query CreateQuery(HybridScenario scenario, string topic, string conflictingTopic, float[] vector)
    {
        return scenario switch
        {
            HybridScenario.VectorOnlyAligned => new VectorQuery("emb", vector, topK: FinalTopN),
            HybridScenario.FilteredVectorBroad => new VectorQuery("emb", vector, topK: FinalTopN,
                filter: new TermQuery("accessGroup", "public")),
            HybridScenario.FilteredVectorMedium => new VectorQuery("emb", vector, topK: FinalTopN,
                filter: new TermQuery("category", "hybrid-category-00")),
            HybridScenario.FilteredVectorNarrow => new VectorQuery("emb", vector, topK: FinalTopN,
                filter: new TermQuery("accessGroup", "tenant-00")),
            HybridScenario.RrfAligned => new RrfQuery()
                .Add(new TermQuery("body", topic))
                .Add(new VectorQuery("emb", vector, topK: CandidateK)),
            HybridScenario.RrfConflict => new RrfQuery()
                .Add(new TermQuery("body", conflictingTopic))
                .Add(new VectorQuery("emb", vector, topK: CandidateK)),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
    }

    private static void CheckFilterSelectivity(HybridRecord[] records)
    {
        if (records.Length != 20_000)
            return;
        CheckShare("BroadFilter", records.Count(static record => record.AccessGroup == "public"), records.Length, 0.45, 0.55);
        CheckShare("MediumFilter", records.Count(static record => record.Category == "hybrid-category-00"), records.Length, 0.02, 0.05);
        CheckShare("NarrowFilter", records.Count(static record => record.AccessGroup == "tenant-00"), records.Length, 0.0003, 0.003);
    }

    private static void CheckShare(string name, int count, int total, double minimum, double maximum)
    {
        var share = (double)count / total;
        if (share < minimum || share > maximum)
            throw new InvalidOperationException($"{name} selectivity {share:P3} is outside {minimum:P3}–{maximum:P3}.");
    }

    private void PreflightVectorFilters(
        HybridRecord[] records,
        VectorRecord[] vectorRecords,
        VectorQueryCase queryCase)
    {
        foreach (var scenario in new[]
                 {
                     HybridScenario.FilteredVectorBroad,
                     HybridScenario.FilteredVectorMedium,
                     HybridScenario.FilteredVectorNarrow
                 })
        {
            var allowed = scenario switch
            {
                HybridScenario.FilteredVectorBroad => records.Where(static item => item.AccessGroup == "public")
                    .Select(static item => item.Ordinal).ToHashSet(),
                HybridScenario.FilteredVectorMedium => records.Where(static item => item.Category == "hybrid-category-00")
                    .Select(static item => item.Ordinal).ToHashSet(),
                _ => records.Where(static item => item.AccessGroup == "tenant-00")
                    .Select(static item => item.Ordinal).ToHashSet()
            };
            var truth = VectorGroundTruth.Compute(vectorRecords, queryCase, FinalTopN, allowed.Contains);
            if (truth.NeighbourOrdinals.Any(ordinal => !allowed.Contains(ordinal)))
                throw new InvalidOperationException($"{scenario} scalar ground truth escaped its filter.");
            var query = CreateQuery(scenario, LeanCorpusHybridProfile.Topics[0],
                LeanCorpusHybridProfile.Topics[1], queryCase.Vector);
            var results = _searcher!.Search(query, FinalTopN);
            if (results.ScoreDocs.Any(item => !allowed.Contains(item.DocId)))
                throw new InvalidOperationException($"{scenario} vector query escaped its filter.");
        }
    }

    private void PreflightRrf(
        HybridRecord[] records,
        LeanCorpusHybridProfile profile,
        VectorRecord[] vectorRecords,
        VectorQueryCase queryCase)
    {
        var topic = LeanCorpusHybridProfile.Topics[0];
        var conflictTopic = LeanCorpusHybridProfile.Topics[1];
        var exactTop50 = VectorGroundTruth.Compute(vectorRecords, queryCase, CandidateK)
            .NeighbourOrdinals.ToHashSet();
        var aligned = _searcher!.Search(CreateQuery(HybridScenario.RrfAligned, topic, conflictTopic,
            queryCase.Vector), CandidateK);
        if (!aligned.ScoreDocs.Take(FinalTopN).Any(item =>
                records[item.DocId].Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(topic) &&
                exactTop50.Contains(item.DocId)))
            throw new InvalidOperationException("Aligned RRF has no text and exact vector overlap in its final Top-10.");

        if (!records.Any(record =>
                profile.Classify(42, record.Ordinal) == HybridCorrelation.Conflict &&
                LeanCorpusHybridProfile.Topics.Any(textTopic => textTopic != record.Topic &&
                    record.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(textTopic))))
            throw new InvalidOperationException("The hybrid conflict fixture has no opposing text and vector topic.");
        var conflict = _searcher.Search(CreateQuery(HybridScenario.RrfConflict, topic, conflictTopic,
            queryCase.Vector), CandidateK);
        if (conflict.ScoreDocs.Length == 0)
            throw new InvalidOperationException("Conflict RRF returned no results.");
    }

    private static void RegisterIdentity(
        LeanCorpusHybridProfile profile,
        DataForgeGenerationOptions options,
        HybridRecord[] records)
    {
        using var writer = new CanonicalJsonWriter(Stream.Null);
        foreach (var record in records)
        {
            profile.CanonicalRecordWriter.Write(writer, record);
            writer.WriteLine();
        }
        BenchmarkDatasetSidecars.Write(new DataForgeDatasetIdentity(
            DataForgeSourceKind.Generated,
            DataForgeVersions.DataForgeVersion,
            profile.Descriptor.ProfileId,
            profile.Descriptor.ProfileVersion,
            DatasetId: null,
            DatasetVersion: null,
            Seed: options.Seed,
            RecordCount: records.Length,
            Parameters: options.Parameters,
            ContentSha256: Convert.ToHexString(writer.GetSha256()).ToLowerInvariant()));
    }

    private static int Checksum(IEnumerable<int> documentIds)
    {
        var checksum = 17;
        foreach (var documentId in documentIds)
            checksum = unchecked(checksum * 31 + documentId);
        return checksum;
    }
}
