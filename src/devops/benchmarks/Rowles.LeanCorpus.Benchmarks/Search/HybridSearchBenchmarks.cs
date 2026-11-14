using System.Globalization;
using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
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
    private const double ScoreTolerance = 1e-5;
    private static readonly HashSet<string> IdField = new(StringComparer.Ordinal) { "id" };

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
            NormaliseVectors = true,
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
        CheckShare("BroadFilter", records.Count(static record => record.AccessGroup == "public"), records.Length, 0.45, 0.55);
        CheckShare("MediumFilter", records.Count(static record => record.Category == "hybrid-category-00"), records.Length, 0.02, 0.05);
        CheckShare("NarrowFilter", records.Count(static record => record.AccessGroup == "tenant-00"), records.Length, 0.0003, 0.003);
    }

    private static void CheckShare(string name, int count, int total, double minimum, double maximum)
    {
        var share = (double)count / total;
        if (share < minimum || share > maximum)
            throw new InvalidOperationException($"{name} selectivity {share:P3} is outside {minimum:P3} to {maximum:P3}.");
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"// Hybrid preflight: {name} cardinality {count} of {total} ({share:P3})."));
    }

    private void PreflightVectorFilters(
        HybridRecord[] records,
        VectorRecord[] vectorRecords,
        VectorQueryCase queryCase)
    {
        var oracle = ComputeOracleScores(vectorRecords, queryCase.Vector);
        var unfiltered = VectorGroundTruth.Compute(vectorRecords, queryCase, FinalTopN);
        var unfilteredResults = _searcher!.Search(CreateQuery(HybridScenario.VectorOnlyAligned,
            LeanCorpusHybridProfile.Topics[0], LeanCorpusHybridProfile.Topics[1], queryCase.Vector), FinalTopN);
        CompareWithGroundTruth(HybridScenario.VectorOnlyAligned, records, unfilteredResults.ScoreDocs, unfiltered, oracle, null);
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"// Hybrid preflight: VectorOnlyAligned candidates {unfilteredResults.TotalHits}, exact Top-{FinalTopN} matches ground truth."));

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
            if (results.TotalHits != allowed.Count)
                throw new InvalidOperationException(
                    $"{scenario} scanned {results.TotalHits} candidates, but its filter allows {allowed.Count}.");
            CompareWithGroundTruth(scenario, records, results.ScoreDocs, truth, oracle, allowed);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"// Hybrid preflight: {scenario} filter cardinality {allowed.Count}, candidates {results.TotalHits}, exact Top-{FinalTopN} matches ground truth."));
        }
    }

    /// <summary>
    /// Compares engine neighbours with the scalar double-precision oracle. The engine scores
    /// in single precision, so scores agree within <see cref="ScoreTolerance"/>. Neighbour IDs
    /// must match exactly except where the oracle itself separates two documents by less than
    /// that tolerance. Equal engine scores must be ordered by ascending document ID.
    /// </summary>
    private void CompareWithGroundTruth(
        HybridScenario scenario,
        HybridRecord[] records,
        Search.Scoring.ScoreDoc[] engine,
        VectorGroundTruthResult truth,
        double[] oracle,
        HashSet<long>? allowed)
    {
        var expected = truth.NeighbourOrdinals;
        if (engine.Length != expected.Length)
            throw new InvalidOperationException(
                $"{scenario} returned {engine.Length} neighbours; ground truth has {expected.Length}.");

        var seen = new HashSet<int>();
        for (var rank = 0; rank < engine.Length; rank++)
        {
            var docId = engine[rank].DocId;
            if (!seen.Add(docId))
                throw new InvalidOperationException($"{scenario} returned document {docId} twice.");
            var storedId = _searcher!.GetStoredFields(docId, IdField)["id"][0];
            if (!string.Equals(storedId, records[docId].Id, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"{scenario} document {docId} stores ID {storedId}, expected {records[docId].Id}.");
            if (allowed is not null && !allowed.Contains(docId))
                throw new InvalidOperationException($"{scenario} document {docId} is outside its filter.");

            var truthScore = truth.CosineValues[rank];
            if (Math.Abs(engine[rank].Score - truthScore) > ScoreTolerance)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"{scenario} rank {rank + 1} scores {engine[rank].Score:R}; ground truth scores {truthScore:R}."));
            if (Math.Abs(oracle[docId] - truthScore) > ScoreTolerance)
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"{scenario} rank {rank + 1} is document {docId} with oracle score {oracle[docId]:R}; ground truth has {expected[rank]} at {truthScore:R}."));

            var nearTie = (rank > 0 && truth.CosineValues[rank - 1] - truthScore <= ScoreTolerance)
                || (rank + 1 < truth.CosineValues.Length && truthScore - truth.CosineValues[rank + 1] <= ScoreTolerance)
                || (rank + 1 == truth.CosineValues.Length && HasOracleTieOutsideTruth(truth, oracle, allowed, truthScore));
            if (!nearTie && docId != expected[rank])
                throw new InvalidOperationException(
                    $"{scenario} rank {rank + 1} is document {docId}; ground truth has {expected[rank]}.");

            if (rank > 0 && SameScore(engine[rank - 1].Score, engine[rank].Score) && engine[rank - 1].DocId > docId)
                throw new InvalidOperationException(
                    $"{scenario} breaks a score tie at rank {rank + 1} by descending document ID.");
        }
    }

    private static bool HasOracleTieOutsideTruth(
        VectorGroundTruthResult truth,
        double[] oracle,
        HashSet<long>? allowed,
        double lastScore)
    {
        var inTruth = truth.NeighbourOrdinals.ToHashSet();
        for (var ordinal = 0; ordinal < oracle.Length; ordinal++)
        {
            if (inTruth.Contains(ordinal) || (allowed is not null && !allowed.Contains(ordinal)))
                continue;
            if (Math.Abs(oracle[ordinal] - lastScore) <= ScoreTolerance)
                return true;
        }
        return false;
    }

    private static double[] ComputeOracleScores(VectorRecord[] records, float[] query)
    {
        var scores = new double[records.Length];
        double queryNorm = 0;
        foreach (var component in query)
            queryNorm += (double)component * component;
        foreach (var record in records)
        {
            double dot = 0, recordNorm = 0;
            for (var dimension = 0; dimension < query.Length; dimension++)
            {
                dot += (double)record.Vector[dimension] * query[dimension];
                recordNorm += (double)record.Vector[dimension] * record.Vector[dimension];
            }
            scores[record.Ordinal] = dot / Math.Sqrt(recordNorm * queryNorm);
        }
        return scores;
    }

    private void PreflightRrf(
        HybridRecord[] records,
        LeanCorpusHybridProfile profile,
        VectorRecord[] vectorRecords,
        VectorQueryCase queryCase)
    {
        var topic = LeanCorpusHybridProfile.Topics[0];
        var conflictTopic = LeanCorpusHybridProfile.Topics[1];
        var oracle = ComputeOracleScores(vectorRecords, queryCase.Vector);
        var exactTop50Truth = VectorGroundTruth.Compute(vectorRecords, queryCase, CandidateK);
        var exactTop50 = exactTop50Truth.NeighbourOrdinals.ToHashSet();

        if (!records.Any(record =>
                profile.Classify(42, record.Ordinal) == HybridCorrelation.Conflict &&
                LeanCorpusHybridProfile.Topics.Any(textTopic => textTopic != record.Topic &&
                    record.Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(textTopic))))
            throw new InvalidOperationException("The hybrid conflict fixture has no opposing text and vector topic.");

        foreach (var (scenario, textTopic) in new[]
                 {
                     (HybridScenario.RrfAligned, topic),
                     (HybridScenario.RrfConflict, conflictTopic)
                 })
        {
            var rrf = (RrfQuery)CreateQuery(scenario, topic, conflictTopic, queryCase.Vector);
            if (rrf.K != 60 || rrf.Queries.Count != 2)
                throw new InvalidOperationException($"{scenario} fusion parameters changed: k={rrf.K}, branches={rrf.Queries.Count}.");

            // Freeze each branch list once, prove the vector branch against the oracle and the
            // text branch against the corpus, then fuse independently of RrfQuery.Combine.
            var textBranch = _searcher!.Search(rrf.Queries[0], CandidateK).ScoreDocs;
            var vectorBranch = _searcher.Search(rrf.Queries[1], CandidateK).ScoreDocs;
            CompareWithGroundTruth(scenario, records, vectorBranch, exactTop50Truth, oracle, null);
            if (textBranch.Length == 0)
                throw new InvalidOperationException($"{scenario} text branch returned no results.");
            if (textBranch.Any(item =>
                    !records[item.DocId].Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(textTopic)))
                throw new InvalidOperationException($"{scenario} text branch returned a document without '{textTopic}'.");

            var expected = FuseReciprocalRanks([textBranch, vectorBranch], rrf.K);
            var fused = _searcher.Search(rrf, CandidateK);
            CompareFusedOrder(scenario.ToString(), fused.ScoreDocs, expected, CandidateK);

            var finalTop = fused.ScoreDocs.Take(FinalTopN).ToArray();
            var overlap = finalTop.Count(item =>
                records[item.DocId].Body.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(textTopic) &&
                exactTop50.Contains(item.DocId));
            if (scenario == HybridScenario.RrfAligned && overlap == 0)
                throw new InvalidOperationException("Aligned RRF has no text and exact vector overlap in its final Top-10.");
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"// Hybrid preflight: {scenario} text branch {textBranch.Length}, vector branch {vectorBranch.Length}, fused {fused.ScoreDocs.Length} of {fused.TotalHits}, final Top-{FinalTopN} overlap {overlap}; fused order matches independent RRF."));
        }
    }

    /// <summary>
    /// Independent reciprocal rank fusion: score(d) = sum of 1 / (k + rank), rank one-based,
    /// accumulated in branch order in single precision as the engine does. Returns every fused
    /// document, best first, so a tie cut by the result limit can be checked against all its members.
    /// </summary>
    internal static (int DocId, float Score)[] FuseReciprocalRanks(Search.Scoring.ScoreDoc[][] branches, int k)
    {
        var scores = new SortedDictionary<int, float>();
        foreach (var branch in branches)
        {
            for (var rank = 1; rank <= branch.Length; rank++)
            {
                var docId = branch[rank - 1].DocId;
                scores[docId] = (scores.TryGetValue(docId, out var current) ? current : 0f) + 1.0f / (k + rank);
            }
        }
        return scores.Select(static pair => (pair.Key, pair.Value))
            .OrderByDescending(static item => item.Value)
            .ThenBy(static item => item.Key)
            .ToArray();
    }

    /// <summary>
    /// RRF does not define an order inside a group of equal fused scores. The engine's top
    /// <paramref name="topN"/> must have the reference's score sequence exactly and no duplicate
    /// documents; each complete tie group must hold the same documents, and a group cut by the limit
    /// may hold any of the reference's documents with that score, drawn from the complete fused set.
    /// </summary>
    internal static void CompareFusedOrder(
        string scenario,
        Search.Scoring.ScoreDoc[] fused,
        (int DocId, float Score)[] complete,
        int topN)
    {
        var expected = complete.Take(topN).ToArray();
        if (fused.Length != expected.Length)
            throw new InvalidOperationException(
                $"{scenario} fused {fused.Length} documents; independent RRF has {expected.Length}.");
        if (fused.Select(static item => item.DocId).Distinct().Count() != fused.Length)
            throw new InvalidOperationException($"{scenario} fused results contain a duplicate document.");
        for (var rank = 0; rank < fused.Length; rank++)
        {
            if (!SameScore(fused[rank].Score, expected[rank].Score))
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                    $"{scenario} fused rank {rank + 1} scores {fused[rank].Score:R}; independent RRF scores {expected[rank].Score:R}."));
        }

        var start = 0;
        while (start < fused.Length)
        {
            var end = start;
            while (end + 1 < fused.Length && SameScore(fused[end + 1].Score, fused[start].Score))
                end++;
            var actualGroup = fused[start..(end + 1)].Select(static item => item.DocId).ToHashSet();
            if (end + 1 == fused.Length && complete.Length > fused.Length)
            {
                // The limit may cut this group, so its members come from every reference document
                // with the same score, including those beyond the limit.
                var score = fused[start].Score;
                var candidates = complete.Where(item => SameScore(item.Score, score)).Select(static item => item.DocId).ToHashSet();
                if (!actualGroup.IsSubsetOf(candidates))
                    throw new InvalidOperationException($"{scenario} fused tie group at rank {start + 1} holds a document outside the independent RRF tie.");
            }
            else if (!actualGroup.SetEquals(expected[start..(end + 1)].Select(static item => item.DocId)))
            {
                throw new InvalidOperationException($"{scenario} fused tie group at rank {start + 1} differs from independent RRF.");
            }
            start = end + 1;
        }
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
            Dependencies: profile.Dependencies.ToArray(),
            ContentSha256: Convert.ToHexString(writer.GetSha256()).ToLowerInvariant()));
    }

    /// <summary>
    /// Exact score identity. Fused scores are sums of the same single-precision terms in the
    /// same order, and engine ties are exact, so a tolerance would hide a real difference.
    /// </summary>
    internal static bool SameScore(float left, float right)
        => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right);

    private static int Checksum(IEnumerable<int> documentIds)
    {
        var checksum = 17;
        foreach (var documentId in documentIds)
            checksum = unchecked(checksum * 31 + documentId);
        return checksum;
    }
}
