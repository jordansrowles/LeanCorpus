using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index.Migration;

[Trait("Category", "Index")]
[Trait("Category", "Migration")]
public sealed class HistoricalIndexFixtureTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public HistoricalIndexFixtureTests(TestDirectoryFixture fixture) => _fixture = fixture;

    public static TheoryData<HistoricalIndexFixture, bool, bool, int> Fixtures => new()
    {
        { HistoricalIndexFixture.Version200Loose, true, false, 3 },
        { HistoricalIndexFixture.Version230Loose, true, false, 3 },
        { HistoricalIndexFixture.Version230Compound, true, true, 4 },
        { HistoricalIndexFixture.Version300CurrentLoose, false, false, 3 },
    };

    [Theory(DisplayName = "Historical full index: inspect, search, validate and migrate")]
    [MemberData(nameof(Fixtures))]
    public void FullIndex_InspectSearchValidateAndMigrate(
        HistoricalIndexFixture fixture,
        bool requiresMigration,
        bool expectedCompound,
        int expectedLiveDocuments)
    {
        string path = HistoricalIndexFixtures.Extract(
            fixture,
            Path.Combine(_fixture.Path, fixture.ToString()));

        using var directory = new MMapDirectory(path);
        var inventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions
        {
            IncludeChecksums = true,
        });
        Assert.DoesNotContain(inventory.Issues, issue => issue.Severity == IndexCheckSeverity.Error);
        Assert.Single(inventory.Segments);
        Assert.Equal(4, inventory.Segments[0].DocCount);
        Assert.Equal(expectedLiveDocuments, inventory.Segments[0].LiveDocCount);

        var compatibility = IndexCompatibility.Check(directory);
        Assert.True(compatibility.CanRead);
        Assert.True(compatibility.CanValidate);
        Assert.Equal(requiresMigration, compatibility.CanMigrate);
        if (requiresMigration)
        {
            var strictCompatibility = IndexCompatibility.Check(directory, new IndexCompatibilityOptions
            {
                RequireCurrentFormats = true,
            });
            Assert.True(strictCompatibility.RequiresMigration);
        }

        var validation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.True(validation.IsHealthy, FormatIssues(validation));
        var before = CaptureLogicalResults(path);

        var plan = IndexCodecMigrator.Plan(directory);
        if (requiresMigration)
            Assert.Contains(plan.Actions, action => action.Kind != IndexCodecMigrationActionKind.NoOp);
        else
            Assert.All(plan.Actions, action => Assert.Equal(IndexCodecMigrationActionKind.NoOp, action.Kind));

        if (requiresMigration)
        {
            var migration = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = true,
                ValidateAfterMigration = true,
            });
            Assert.True(migration.Succeeded, FormatIssues(migration));
            Assert.NotNull(migration.ValidationResult);
            Assert.True(migration.ValidationResult.IsHealthy, FormatIssues(migration.ValidationResult));
        }

        Assert.Equal(before, CaptureLogicalResults(path));

        using var migratedDirectory = new MMapDirectory(path);
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(migratedDirectory).Status);

        var currentInventory = IndexFormatInspector.Inspect(
            migratedDirectory,
            new IndexFormatInspectionOptions { IncludeChecksums = true });
        foreach (var file in currentInventory.Segments.SelectMany(segment => segment.Files))
        {
            if (file.FormatVersion.HasValue)
            {
                Assert.True(
                    file.FrameKind is CodecFileFrameKind.Canonical or CodecFileFrameKind.Container,
                    $"Unexpected frame kind {file.FrameKind} for {file.FileName}.");
                Assert.Equal(CodecFileWriter.CurrentFrameVersion, file.FrameVersion);
                Assert.True(file.IsCurrent, file.FileName);
                Assert.Equal(
                    file.FrameKind == CodecFileFrameKind.Canonical
                        ? CodecChecksumStatus.Valid
                        : CodecChecksumStatus.NotApplicable,
                    file.ChecksumStatus);
            }
        }

        var segmentId = currentInventory.SegmentIds.Single();
        var segmentInfo = SegmentInfo.ReadFrom(Path.Combine(path, segmentId + ".seg"));
        Assert.Equal(expectedCompound, segmentInfo.IsCompoundFile);
    }

    private static LogicalResults CaptureLogicalResults(string path)
    {
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var text = searcher.Search(new TermQuery("body", "historical"), 10);
        var ids = text.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single())
            .Order(StringComparer.Ordinal)
            .ToArray();
        var categories = searcher.Search(new TermQuery("category", "shared"), 10);
        var vectors = searcher.Search(
            new VectorQuery("embedding", new float[] { 1, 2, 3, 4 }, topK: 3, efSearch: 16),
            3);
        var vectorIds = vectors.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single())
            .ToArray();
        var segment = searcher.GetSegmentReaders().Single();
        var categoryValues = segment.GetSortedSetDocValues("category")!
            .Select(static values => string.Join("|", values))
            .ToArray();
        var scoreValues = segment.GetSortedNumericDocValues("score")!
            .Select(static values => string.Join("|", values.Select(value => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))
            .ToArray();
        var payloadValues = new string[segment.MaxDoc];
        for (int docId = 0; docId < payloadValues.Length; docId++)
        {
            Assert.True(segment.TryGetBinaryDocValues("payload", docId, out var payloads));
            payloadValues[docId] = string.Join("|", payloads.Select(Convert.ToHexString));
        }

        return new LogicalResults(
            text.TotalHits,
            ids,
            categories.TotalHits,
            vectorIds,
            categoryValues,
            scoreValues,
            payloadValues);
    }

    private static string FormatIssues(IndexCheckResult result)
        => string.Join("; ", result.DetailedIssues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FormatIssues(IndexCodecMigrationResult result)
        => string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private sealed class LogicalResults : IEquatable<LogicalResults>
    {
        public LogicalResults(
            int textHits,
            string[] storedIds,
            int categoryHits,
            string[] vectorIds,
            string[] categoryValues,
            string[] scoreValues,
            string[] payloadValues)
        {
            TextHits = textHits;
            StoredIds = storedIds;
            CategoryHits = categoryHits;
            VectorIds = vectorIds;
            CategoryValues = categoryValues;
            ScoreValues = scoreValues;
            PayloadValues = payloadValues;
        }

        public int TextHits { get; }

        public string[] StoredIds { get; }

        public int CategoryHits { get; }

        public string[] VectorIds { get; }

        public string[] CategoryValues { get; }

        public string[] ScoreValues { get; }

        public string[] PayloadValues { get; }

        public bool Equals(LogicalResults? other)
            => other is not null
                && TextHits == other.TextHits
                && CategoryHits == other.CategoryHits
                && StoredIds.SequenceEqual(other.StoredIds, StringComparer.Ordinal)
                && VectorIds.SequenceEqual(other.VectorIds, StringComparer.Ordinal)
                && CategoryValues.SequenceEqual(other.CategoryValues, StringComparer.Ordinal)
                && ScoreValues.SequenceEqual(other.ScoreValues, StringComparer.Ordinal)
                && PayloadValues.SequenceEqual(other.PayloadValues, StringComparer.Ordinal);

        public override bool Equals(object? obj) => Equals(obj as LogicalResults);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(TextHits);
            hash.Add(CategoryHits);
            foreach (string id in StoredIds)
                hash.Add(id, StringComparer.Ordinal);
            foreach (string id in VectorIds)
                hash.Add(id, StringComparer.Ordinal);
            foreach (string value in CategoryValues)
                hash.Add(value, StringComparer.Ordinal);
            foreach (string value in ScoreValues)
                hash.Add(value, StringComparer.Ordinal);
            foreach (string value in PayloadValues)
                hash.Add(value, StringComparer.Ordinal);
            return hash.ToHashCode();
        }
    }
}
