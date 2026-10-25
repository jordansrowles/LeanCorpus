using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index.Migration;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
[Area(TestArea.CodecKit)]
public sealed class SparseVector311MigrationAcceptanceTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"sparse-vector-311-migration-{Guid.NewGuid():N}");

    public SparseVector311MigrationAcceptanceTests() => Directory.CreateDirectory(_path);

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Theory]
    [InlineData("float32-loose", VectorQuantisation.None, false, ".vec")]
    [InlineData("float32-compound", VectorQuantisation.None, true, ".vec")]
    [InlineData("int8-loose", VectorQuantisation.Int8, false, ".vq")]
    [InlineData("int8-compound", VectorQuantisation.Int8, true, ".vq")]
    public void Package311SparseVectorFixtureMigratesPresenceFromHnsw(
        string fixtureDirectory,
        VectorQuantisation quantisation,
        bool compound,
        string vectorExtension)
    {
        string extracted = HistoricalIndexFixtures.Extract(
            HistoricalIndexFixture.Version311SparseHnsw,
            Path.Combine(_path, "fixture"));
        string indexPath = Path.Combine(extracted, fixtureDirectory);
        using var directory = new MMapDirectory(indexPath);

        IndexCompatibilityResult compatibility = IndexCompatibility.Check(directory);
        Assert.True(compatibility.CanRead);
        Assert.True(compatibility.CanValidate);
        Assert.True(compatibility.CanMigrate);

        VectorDocumentState[] before = CaptureVectorState(indexPath);
        Assert.Equal(new[] { true, true, false, true, true, false }, before.Select(static state => state.HasVector));
        Assert.All(before.Where(static state => state.HasVector), static state => Assert.NotNull(state.Values));
        Assert.All(before.Where(static state => !state.HasVector), static state => Assert.Null(state.Values));
        AssertVectorQuery(indexPath);

        var sourceSegments = IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos;
        Assert.Equal(2, sourceSegments.Count);
        foreach (SegmentInfo segmentInfo in sourceSegments)
        {
            Assert.Equal(compound, segmentInfo.IsCompoundFile);
            SegmentReader reader = new(directory, segmentInfo);
            try
            {
                Assert.Equal(3, reader.MaxDoc);
                Assert.True(Assert.Single(reader.Info.VectorFields).HasHnsw);
                AssertVectorFileVersion(directory, segmentInfo, quantisation, vectorExtension, expectedVersion: 1);
                Assert.Equal(new[] { 0, 1 }, reader.GetHnswGraph("embedding")!.GetNodesAtLevel(0).Order().ToArray());
                Assert.True(reader.HasVector("embedding", 0));
                Assert.True(reader.HasVector("embedding", 1));
                Assert.False(reader.HasVector("embedding", 2));
            }
            finally
            {
                reader.Dispose();
            }
        }

        IndexCheckResult beforeValidation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.True(beforeValidation.IsHealthy, FormatIssues(beforeValidation));

        IndexCodecMigrationPlan plan = IndexCodecMigrator.Plan(directory);
        Assert.True(plan.CanExecute, string.Join("; ", plan.Actions
            .Where(static action => !action.CanExecute)
            .Select(static action => action.ReasonCannotExecute)));
        var vectorActions = plan.Actions
            .Where(action => action.FileName?.EndsWith(vectorExtension, StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(2, vectorActions.Length);
        Assert.All(vectorActions, action =>
        {
            Assert.Equal((byte)1, action.FromVersion);
            Assert.Equal((byte)2, action.ToVersion);
            Assert.Equal(IndexCodecMigrationActionKind.Rewrite, action.Kind);
            Assert.True(action.CanExecute, action.ReasonCannotExecute);
        });

        IndexCodecMigrationResult dryRun = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = true,
            ValidateBeforeMigration = true,
            ValidateAfterMigration = true,
        });
        Assert.True(dryRun.Succeeded, FormatIssues(dryRun));
        Assert.True(dryRun.DryRun);
        AssertVectorStateEqual(before, CaptureVectorState(indexPath));
        AssertVectorQuery(indexPath);

        IndexCodecMigrationResult migration = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            ValidateBeforeMigration = true,
            ValidateAfterMigration = true,
        });
        Assert.True(migration.Succeeded, FormatIssues(migration));
        Assert.NotNull(migration.ValidationResult);
        Assert.True(migration.ValidationResult.IsHealthy, FormatIssues(migration.ValidationResult));
        AssertVectorStateEqual(before, CaptureVectorState(indexPath));
        AssertVectorQuery(indexPath);

        SegmentInfo[] migratedSegments = IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos.ToArray();
        Assert.Equal(2, migratedSegments.Length);
        foreach (SegmentInfo segmentInfo in migratedSegments)
        {
            using var reader = new SegmentReader(directory, segmentInfo);
            AssertVectorFileVersion(directory, segmentInfo, quantisation, vectorExtension, expectedVersion: 2);
            Assert.Equal(new[] { 0, 1 }, reader.GetHnswGraph("embedding")!.GetNodesAtLevel(0).Order().ToArray());
            Assert.False(reader.HasVector("embedding", 2));
        }

        using (var writer = new IndexWriter(directory, CreateMergeConfig(quantisation, compound)))
        {
            writer.ForceMerge(1);
            writer.Commit();
        }

        VectorDocumentState[] afterMerge = CaptureVectorState(indexPath);
        Assert.Equal(before.Select(static state => state.HasVector), afterMerge.Select(static state => state.HasVector));
        Assert.All(afterMerge.Where(static state => state.HasVector), static state => Assert.NotNull(state.Values));
        Assert.All(afterMerge.Where(static state => !state.HasVector), static state => Assert.Null(state.Values));
        AssertVectorQuery(indexPath);

        var mergedSegments = IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos;
        SegmentInfo mergedInfo = Assert.Single(mergedSegments);
        Assert.Equal(compound, mergedInfo.IsCompoundFile);
        using (var mergedReader = new SegmentReader(directory, mergedInfo))
        {
            Assert.Equal(new[] { 0, 1, 3, 4 }, mergedReader.GetHnswGraph("embedding")!.GetNodesAtLevel(0).Order().ToArray());
            AssertVectorFileVersion(directory, mergedInfo, quantisation, vectorExtension, expectedVersion: 2);
        }

        IndexCheckResult afterMergeValidation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.True(afterMergeValidation.IsHealthy, FormatIssues(afterMergeValidation));
    }

    private static IndexWriterConfig CreateMergeConfig(VectorQuantisation quantisation, bool compound)
        => new()
        {
            BuildHnswOnFlush = true,
            HnswSeed = 42,
            MergePolicy = NoMergePolicy.Instance,
            NormaliseVectors = false,
            UseCompoundFile = compound,
            VectorQuantisation = quantisation,
        };

    private static VectorDocumentState[] CaptureVectorState(string path)
    {
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        return searcher.GetSegmentReaders()
            .SelectMany(static reader => Enumerable.Range(0, reader.MaxDoc)
                .Select(localDocId => new VectorDocumentState(
                    reader.HasVector("embedding", localDocId),
                    reader.GetVector("embedding", localDocId))))
            .ToArray();
    }

    private static void AssertVectorStateEqual(
        IReadOnlyList<VectorDocumentState> expected,
        IReadOnlyList<VectorDocumentState> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int docId = 0; docId < expected.Count; docId++)
        {
            Assert.Equal(expected[docId].HasVector, actual[docId].HasVector);
            if (expected[docId].Values is { } expectedValues)
                Assert.Equal(expectedValues, actual[docId].Values);
            else
                Assert.Null(actual[docId].Values);
        }
    }

    private static void AssertVectorQuery(string path)
    {
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        TopDocs hits = searcher.Search(
            new VectorQuery("embedding", [1, 0], topK: 6, efSearch: 16),
            6,
            TestContext.Current.CancellationToken);
        Assert.Equal(new[] { 0, 1, 3, 4 }, hits.ScoreDocs.Select(static hit => hit.DocId).Order().ToArray());
    }

    private static void AssertVectorFileVersion(
        MMapDirectory directory,
        SegmentInfo segmentInfo,
        VectorQuantisation quantisation,
        string extension,
        byte expectedVersion)
    {
        string fileName = quantisation == VectorQuantisation.None
            ? VectorFilePaths.VectorFile(segmentInfo.SegmentId, "embedding")
            : VectorFilePaths.QuantisedVectorFile(segmentInfo.SegmentId, "embedding");
        CodecFileDescriptor descriptor = quantisation == VectorQuantisation.None
            ? VectorCodecFiles.Float32
            : VectorCodecFiles.Quantised;
        Assert.EndsWith(extension, fileName, StringComparison.Ordinal);

        using ISegmentFileSource source = segmentInfo.IsCompoundFile
            ? new CompoundSegmentFileSource(directory, segmentInfo.SegmentId)
            : new LooseSegmentFileSource(directory, segmentInfo.SegmentId);
        using var input = source.OpenInput(fileName);
        using var frame = CodecFileReader.OpenSupported(input, descriptor);
        Assert.Equal(expectedVersion, frame.FormatVersion);
        _ = frame.ReadBody();
    }

    private static string FormatIssues(IndexCheckResult result)
        => string.Join("; ", result.DetailedIssues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private static string FormatIssues(IndexCodecMigrationResult result)
        => string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}"));

    private readonly record struct VectorDocumentState(bool HasVector, float[]? Values);
}
