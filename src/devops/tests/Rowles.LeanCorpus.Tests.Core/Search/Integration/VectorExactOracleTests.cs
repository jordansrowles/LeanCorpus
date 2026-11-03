using System.Globalization;
using Rowles.LeanCorpus.Tests.Core.Foundation.Unit;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
[Collection(LeanCorpusDefaultsCollection.Name)]
public sealed class VectorExactOracleTests(TestDirectoryFixture fixture) : IClassFixture<TestDirectoryFixture>
{
    private const double ScoreTolerance = 1e-5;
    private const double TieTolerance = 1e-6;

    [Theory]
    [InlineData(32)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(384)]
    [InlineData(768)]
    public void ExactFloat32_MatchesIndependentOracleAcrossSupportedDimensions(int dimension)
    {
        var random = new Random(4_200 + dimension);
        float[] queryVector = RandomVector(random, dimension);
        var documents = Enumerable.Range(0, 24)
            .Select(ordinal => new OracleDocument(
                ordinal,
                RandomVector(random, dimension),
                "all",
                1f))
            .ToArray();

        using var directory = new MMapDirectory(SubDir($"exact-{dimension}"));
        using (var writer = new IndexWriter(directory, ExactConfig(normaliseVectors: true)))
        {
            AddDocuments(writer, documents);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        Assert.Single(searcher.GetSegmentReaders());
        Assert.Null(searcher.GetSegmentReaders()[0].GetHnswGraph("embedding"));
        AssertOracleTopK(searcher, documents, queryVector, topK: 10, queryBoost: 1f, filterToKeep: false);
    }

    [Fact]
    public void ExactFloat32_UsesBoundaryTiesAndKeepsTieOrderingDeterministic()
    {
        OracleDocument[] documents =
        [
            new(0, [1f, 0f], "all", 1f),
            new(1, [0.5f, 0.8660254f], "all", 1f),
            new(2, [0.5f, 0.8660254f], "all", 1f),
            new(3, [0.5f, 0.8660254f], "all", 1f),
            new(4, [-0.5f, 0.8660254f], "all", 1f),
            new(5, [-1f, 0f], "all", 1f),
            new(6, [0f, 0f], "all", 1f),
            new(7, null, "all", 1f),
            new(8, [1f, 1f], "all", 1f),
        ];

        using var directory = new MMapDirectory(SubDir(nameof(ExactFloat32_UsesBoundaryTiesAndKeepsTieOrderingDeterministic)));
        using (var writer = new IndexWriter(directory, ExactConfig(normaliseVectors: false)))
        {
            AddDocuments(writer, documents);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        var first = AssertOracleTopK(searcher, documents, [1f, 0f], topK: 3, queryBoost: 1f, filterToKeep: false);
        var second = AssertOracleTopK(searcher, documents, [1f, 0f], topK: 3, queryBoost: 1f, filterToKeep: false);
        AssertOracleTopK(searcher, documents, [1f, 0f], topK: 3, queryBoost: 1.5f, filterToKeep: false);
        AssertOracleTopK(searcher, documents, [1f, 0f], topK: 3, queryBoost: -1.5f, filterToKeep: false);
        Assert.Equal(
            first.ScoreDocs.Select(hit => SourceOrdinal(searcher, hit.DocId)),
            second.ScoreDocs.Select(hit => SourceOrdinal(searcher, hit.DocId)));

        var allPresent = AssertOracleTopK(searcher, documents, [1f, 0f], topK: 10, queryBoost: 1f, filterToKeep: false);
        Assert.Contains(allPresent.ScoreDocs, hit => hit.Score < 0f);
        Assert.Contains(allPresent.ScoreDocs, hit => hit.Score == 0f);
        Assert.DoesNotContain(allPresent.ScoreDocs, hit => SourceOrdinal(searcher, hit.DocId) == 7);
    }

    [Fact]
    public void ExactFloat32_RespectsMasksBoostsSixSegmentsAndPostMergeOrdinals()
    {
        OracleDocument[] documents =
        [
            new(0, [1f, 0f], "keep", 1f),
            new(1, [0.8f, 0.6f], "keep", 1.5f),
            new(2, [0.8f, 0.6f], "keep", 1.5f),
            new(3, null, "keep", 1f),
            new(4, null, "keep", 1f),
            new(5, null, "keep", 1f),
            new(6, [-1f, 0f], "keep", 1f),
            new(7, [0f, 0f], "keep", 1f),
            new(8, [-0.5f, 0.8660254f], "keep", 1f),
            new(9, [1f, 0f], "keep", 1f, IsLive: false),
            new(10, [0f, 1f], "skip", 1f),
            new(11, [1f, 0f], "skip", 1f),
            new(12, [-0.8f, 0.6f], "keep", 1f),
            new(13, [0.9f, 0.4358899f], "keep", 0.8f),
            new(14, [0f, 0f], "skip", 1f),
            new(15, null, "keep", 1f),
            new(16, [0.8f, 0.6f], "keep", 1.5f),
            new(17, [-1f, 0f], "keep", 1f),
        ];

        using var directory = new MMapDirectory(SubDir(nameof(ExactFloat32_RespectsMasksBoostsSixSegmentsAndPostMergeOrdinals)));
        using (var writer = new IndexWriter(directory, ExactConfig(normaliseVectors: false)))
        {
            for (int segment = 0; segment < 6; segment++)
            {
                AddDocuments(writer, documents.Skip(segment * 3).Take(3));
                writer.Commit();
            }

            writer.DeleteDocuments(new TermQuery("id", "doc-9"));
            writer.Commit();

            using (var beforeMerge = new IndexSearcher(directory, ExactSearcherConfig()))
            {
                Assert.Equal(6, beforeMerge.GetSegmentReaders().Count);
                Assert.All(beforeMerge.GetSegmentReaders(), reader => Assert.Null(reader.GetHnswGraph("embedding")));
                AssertOracleTopK(beforeMerge, documents, [1f, 0f], topK: 8, queryBoost: 2f, filterToKeep: true);
            }

            writer.ForceMerge(1);
            writer.Commit();
        }

        using var afterMerge = new IndexSearcher(directory, ExactSearcherConfig());
        Assert.Single(afterMerge.GetSegmentReaders());
        Assert.Null(afterMerge.GetSegmentReaders()[0].GetHnswGraph("embedding"));
        AssertOracleTopK(afterMerge, documents, [1f, 0f], topK: 8, queryBoost: 2f, filterToKeep: true);
    }

    [Fact]
    public void ExactFloat32_ZeroQueryOnNormalisedFieldReturnsNoMatches()
    {
        OracleDocument[] documents =
        [
            new(0, [1f, 0f], "all", 1f),
            new(1, [0f, 1f], "all", 1f),
            new(2, [0f, 0f], "all", 1f),
            new(3, null, "all", 1f),
        ];

        using var directory = new MMapDirectory(SubDir(nameof(ExactFloat32_ZeroQueryOnNormalisedFieldReturnsNoMatches)));
        using (var writer = new IndexWriter(directory, ExactConfig(normaliseVectors: true)))
        {
            AddDocuments(writer, documents);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        var results = searcher.Search(new VectorQuery("embedding", [0f, 0f], topK: 10), 10);
        Assert.Equal(0, results.TotalHits);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void VectorField_RejectsNonFiniteInput(float value)
        => Assert.Throws<ArgumentException>(() => new VectorField("embedding", new[] { 1f, value }));

    [Fact]
    public void ExactFloat32_QueryDimensionMismatchIsRejected()
    {
        OracleDocument[] documents = [new(0, [1f, 0f], "all", 1f)];
        using var directory = new MMapDirectory(SubDir(nameof(ExactFloat32_QueryDimensionMismatchIsRejected)));
        using (var writer = new IndexWriter(directory, ExactConfig(normaliseVectors: false)))
        {
            AddDocuments(writer, documents);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        var query = new VectorQuery("embedding", [1f, 0f, 0f]);
        ArgumentException exception = Assert.Throws<ArgumentException>(() => searcher.Search(query, 1));
        Assert.Contains("dimension", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => searcher.Explain(query, 0));
    }

    [Fact]
    public void VectorQuery_NegativeBoostReversesHnswCandidateDirection()
    {
        OracleDocument[] documents =
        [
            new(0, [1f, 0f], "all", 1f),
            new(1, [0f, 1f], "all", 1f),
            new(2, [-1f, 0f], "all", 1f),
            new(3, [0f, -1f], "all", 1f),
        ];

        using var directory = new MMapDirectory(SubDir(nameof(VectorQuery_NegativeBoostReversesHnswCandidateDirection)));
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            BuildHnswOnFlush = true,
            NormaliseVectors = false,
            HnswBuildConfig = new HnswBuildConfig { M = 4, M0 = 8, EfConstruction = 16 },
            HnswSeed = 1L,
        }))
        {
            AddDocuments(writer, documents);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(directory, ExactSearcherConfig());
        var query = new VectorQuery("embedding", [1f, 0f], topK: 1, efSearch: 4) { Boost = -1f };
        TopDocs results = searcher.Search(query, 1);

        Assert.Equal(2, SourceOrdinal(searcher, Assert.Single(results.ScoreDocs).DocId));
        Assert.Equal(1f, results.ScoreDocs[0].Score);
    }

    [Fact]
    public void QualificationDefaults_ResolveExpectedFloat32AndInt8SearchBudgets()
    {
        var snapshot = LeanCorpusDefaults.CaptureSnapshotForTests();
        try
        {
            LeanCorpusDefaults.Reset();
            var writerConfig = new IndexWriterConfig();
            var searcherConfig = new IndexSearcherConfig();

            Assert.Equal(16, writerConfig.HnswBuildConfig.M);
            Assert.Equal(32, writerConfig.HnswBuildConfig.EffectiveM0);
            Assert.Equal(100, writerConfig.HnswBuildConfig.EfConstruction);
            Assert.True(writerConfig.BuildHnswOnFlush);
            Assert.True(writerConfig.NormaliseVectors);
            Assert.Equal(VectorQuantisation.None, writerConfig.VectorQuantisation);
            Assert.Null(writerConfig.HnswSeed);
            Assert.False(searcherConfig.EnableQueryCache);

            foreach (int topK in new[] { 1, 10, 100 })
            {
                int expectedEf = Math.Max(64, 4 * topK);
                var query = new VectorQuery("embedding", new float[32], topK);
                Assert.Equal(expectedEf, query.EfSearch);
                Assert.Equal(1, query.OversamplingFactor);
                Assert.True(query.IsOversamplingAutomatic);
                Assert.Equal(topK, query.GetShortlistSize(10_000));
                Assert.Equal(expectedEf, query.GetEffectiveEfSearch(10_000));
            }
        }
        finally
        {
            LeanCorpusDefaults.RestoreSnapshotForTests(snapshot);
        }
    }

    private TopDocs AssertOracleTopK(
        IndexSearcher searcher,
        IReadOnlyList<OracleDocument> documents,
        float[] queryVector,
        int topK,
        float queryBoost,
        bool filterToKeep)
    {
        Query? filter = filterToKeep ? new TermQuery("group", "keep") : null;
        var query = new VectorQuery("embedding", queryVector, topK, filter: filter) { Boost = queryBoost };
        TopDocs actual = searcher.Search(query, topK);

        var expected = documents
            .Where(document => document.IsLive && document.Vector is not null && (!filterToKeep || document.Group == "keep"))
            .Select(document => new ExpectedScore(
                document.SourceOrdinal,
                ScalarCosine(queryVector, document.Vector!) * document.FieldBoost * queryBoost))
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.SourceOrdinal)
            .ToArray();

        int expectedHits = Math.Min(topK, expected.Length);
        Assert.Equal(expected.Length, actual.TotalHits);
        Assert.Equal(expectedHits, actual.ScoreDocs.Length);

        var actualOrdinals = actual.ScoreDocs
            .Select(hit => SourceOrdinal(searcher, hit.DocId))
            .ToArray();
        Assert.Equal(actualOrdinals.Length, actualOrdinals.Distinct().Count());

        var actualByOrdinal = actual.ScoreDocs.ToDictionary(
            hit => SourceOrdinal(searcher, hit.DocId),
            hit => (hit.DocId, hit.Score));
        var expectedByOrdinal = expected.ToDictionary(static item => item.SourceOrdinal, static item => item.Score);

        foreach ((int ordinal, (int docId, float score)) in actualByOrdinal)
        {
            Assert.True(expectedByOrdinal.TryGetValue(ordinal, out double expectedScore), $"Unexpected source ordinal {ordinal}.");
            Assert.True(float.IsFinite(score), $"Production score for source ordinal {ordinal} was not finite.");
            Explanation? explanation = searcher.Explain(query, docId);
            Assert.NotNull(explanation);
            Assert.True(Math.Abs(explanation.Score - expectedScore) <= ScoreTolerance,
                $"Explanation score mismatch for source ordinal {ordinal}: production={explanation.Score:R}, oracle={expectedScore:R}.");
            Assert.True(Math.Abs(score - expectedScore) <= ScoreTolerance,
                $"Score mismatch for source ordinal {ordinal}: production={score:R}, oracle={expectedScore:R}.");
        }

        if (expectedHits == 0)
            return actual;

        double boundary = expected[expectedHits - 1].Score;
        int[] requiredHigher = expected
            .Where(item => item.Score > boundary + TieTolerance)
            .Select(static item => item.SourceOrdinal)
            .ToArray();
        var boundaryTies = expected
            .Where(item => Math.Abs(item.Score - boundary) <= TieTolerance)
            .Select(static item => item.SourceOrdinal)
            .ToHashSet();
        int expectedTieCount = expectedHits - requiredHigher.Length;

        Assert.All(requiredHigher, ordinal => Assert.Contains(ordinal, actualOrdinals));
        Assert.All(actualOrdinals, ordinal =>
        {
            Assert.Contains(ordinal, requiredHigher.Concat(boundaryTies));
        });
        Assert.True(actualOrdinals.Count(boundaryTies.Contains) >= expectedTieCount,
            "The result omitted too many eligible documents tied at the independent Kth score.");
        return actual;
    }

    private static double ScalarCosine(ReadOnlySpan<float> query, ReadOnlySpan<float> document)
    {
        if (query.Length == 0 || query.Length != document.Length)
            return 0d;

        double dot = 0d;
        double queryNormSquared = 0d;
        double documentNormSquared = 0d;
        for (int i = 0; i < query.Length; i++)
        {
            double queryValue = query[i];
            double documentValue = document[i];
            dot += queryValue * documentValue;
            queryNormSquared += queryValue * queryValue;
            documentNormSquared += documentValue * documentValue;
        }

        if (queryNormSquared == 0d || documentNormSquared == 0d)
            return 0d;
        return dot / Math.Sqrt(queryNormSquared * documentNormSquared);
    }

    private void AddDocuments(IndexWriter writer, IEnumerable<OracleDocument> documents)
    {
        foreach (OracleDocument source in documents)
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", $"doc-{source.SourceOrdinal}"));
            document.Add(new StoredField("sourceOrdinal", source.SourceOrdinal));
            document.Add(new StringField("group", source.Group));
            if (source.Vector is not null)
                document.Add(new VectorField("embedding", source.Vector, source.FieldBoost));
            writer.AddDocument(document);
        }
    }

    private int SourceOrdinal(IndexSearcher searcher, int globalDocId)
        => int.Parse(searcher.GetStoredFields(globalDocId)["sourceOrdinal"].Single(), CultureInfo.InvariantCulture);

    private string SubDir(string name)
    {
        string path = Path.Combine(fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static IndexWriterConfig ExactConfig(bool normaliseVectors)
        => new()
        {
            BuildHnswOnFlush = false,
            NormaliseVectors = normaliseVectors,
            MergeThreshold = 100,
            MergeThrottleSegments = 100,
            MaxBufferedDocs = 1_000,
        };

    private static IndexSearcherConfig ExactSearcherConfig()
        => new()
        {
            ParallelSearch = false,
            EnableQueryCache = false,
        };

    private static float[] RandomVector(Random random, int dimension)
    {
        var vector = new float[dimension];
        for (int i = 0; i < vector.Length; i++)
            vector[i] = (float)(random.NextDouble() * 2d - 1d);
        return vector;
    }

    private sealed record OracleDocument(
        int SourceOrdinal,
        float[]? Vector,
        string Group,
        float FieldBoost,
        bool IsLive = true);

    private readonly record struct ExpectedScore(int SourceOrdinal, double Score);
}
