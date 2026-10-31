using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index.Migration;

[Category(TestCategory.Integration)]
[Area(TestArea.CodecKit)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
public sealed class PreV3BbqV2CompatibilityTests : IClassFixture<TestDirectoryFixture>
{
    private const string ManifestSha256 = "f71470b5cb9a53a80807bba3ffd4ba8390a08c09a90556c7348a80824bf4c726";
    private readonly TestDirectoryFixture _fixture;

    public PreV3BbqV2CompatibilityTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Theory(DisplayName = "Pinned pre-v3 BBQ v2 snapshot remains readable in both storage layouts")]
    [InlineData("loose", false)]
    [InlineData("compound", true)]
    public void SnapshotRetainsV2PresenceGraphAndLegacySearchScore(string layout, bool compound)
    {
        string extracted = HistoricalIndexFixtures.Extract(
            HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-v2-{layout}-{Guid.NewGuid():N}"));
        JsonElement manifest = ReadAndVerifyManifest(extracted);
        VerifySnapshotFiles(manifest, extracted);

        string indexPath = Path.Combine(extracted, layout);
        using var directory = new MMapDirectory(indexPath);
        var inventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions
        {
            IncludeChecksums = true,
        });
        Assert.DoesNotContain(inventory.Issues, issue => issue.Severity == IndexCheckSeverity.Error);
        Assert.Equal(2, Assert.Single(inventory.Segments)
            .Files.Single(file => file.FileName.EndsWith(".vq", StringComparison.Ordinal)).FormatVersion);

        var compatibility = IndexCompatibility.Check(directory);
        Assert.True(compatibility.CanRead);
        Assert.True(compatibility.CanValidate);
        Assert.False(compatibility.MustReject);

        SegmentInfo segmentInfo = Assert.Single(IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos);
        Assert.Equal(8, segmentInfo.DocCount);
        Assert.Equal(8, segmentInfo.LiveDocCount);
        Assert.Equal(compound, segmentInfo.IsCompoundFile);
        using var segmentReader = new SegmentReader(directory, segmentInfo);
        Assert.Equal(new[] { true, true, true, false, true, false, true, false },
            Enumerable.Range(0, segmentReader.MaxDoc).Select(docId => segmentReader.HasVector("embedding", docId)));
        Assert.Equal(new[] { 0, 1, 2, 4, 6 },
            segmentReader.GetHnswGraph("embedding")!.GetNodesAtLevel(0).Order().ToArray());

        var validation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.True(validation.IsHealthy,
            string.Join("; ", validation.DetailedIssues.Select(issue => $"{issue.Code}: {issue.Message}")));

        if (!compound)
            AssertLegacyV2Body(indexPath, segmentInfo.SegmentId);

        using var searcher = new IndexSearcher(new MMapDirectory(indexPath));
        var results = searcher.Search(
            new VectorQuery("embedding", CreateBasisVector(128, 0), topK: 5, efSearch: 128),
            topN: 5);
        ScoreDoc best = Assert.Single(results.ScoreDocs.Where(static hit => hit.DocId == 1));
        Assert.Equal(0.09225467f, best.Score, 5);
    }

    [Theory(DisplayName = "BBQ v2 migration wraps its legacy bytes without changing the legacy score")]
    [InlineData("loose", false)]
    [InlineData("compound", true)]
    public void V2MigrationWrapsLegacyPayloadAndPreservesSearchResults(string layout, bool compound)
    {
        string extracted = HistoricalIndexFixtures.Extract(
            HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-migration-{layout}-{Guid.NewGuid():N}"));
        string indexPath = Path.Combine(extracted, layout);
        byte[] originalBody;
        ScoreSnapshot[] originalScores;

        using (var directory = new MMapDirectory(indexPath))
        {
            SegmentInfo segment = Assert.Single(IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos);
            originalBody = ReadQuantisedBody(directory, segment);
            originalScores = CaptureSearchScores(indexPath);

            IndexCodecMigrationPlan plan = IndexCodecMigrator.Plan(directory);
            IndexCodecMigrationAction action = Assert.Single(plan.Actions,
                candidate => candidate.FormatId == "leancorpus.vectors.quantised");
            Assert.Equal((byte)2, action.FromVersion);
            Assert.Equal((byte)3, action.ToVersion);
            Assert.Equal(IndexCodecMigrationActionKind.Rewrite, action.Kind);
            Assert.True(action.CanExecute, action.ReasonCannotExecute);

            IndexCodecMigrationResult migration = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = true,
                ValidateAfterMigration = true,
            });
            Assert.True(migration.Succeeded, string.Join("; ", migration.Issues.Select(issue => issue.Message)));
            Assert.NotNull(migration.ValidationResult);
            Assert.True(migration.ValidationResult.IsHealthy,
                string.Join("; ", migration.ValidationResult.DetailedIssues.Select(issue => issue.Message)));
        }

        byte[] migratedBody;
        using (var directory = new MMapDirectory(indexPath))
        {
            SegmentInfo segment = Assert.Single(IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos);
            migratedBody = ReadQuantisedBody(directory, segment, expectedVersion: 3);
            var validation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
            Assert.True(validation.IsHealthy,
                string.Join("; ", validation.DetailedIssues.Select(issue => issue.Message)));
        }

        const int commonMetadataLength = sizeof(int) + sizeof(int) + sizeof(byte) + 1;
        Assert.Equal(originalBody.AsSpan(0, commonMetadataLength).ToArray(),
            migratedBody.AsSpan(0, commonMetadataLength).ToArray());
        Assert.Equal(2, migratedBody[commonMetadataLength]); // LegacyBbqSign
        Assert.Equal(originalBody.AsSpan(commonMetadataLength).ToArray(),
            migratedBody.AsSpan(commonMetadataLength + 1).ToArray());
        Assert.Equal(originalScores, CaptureSearchScores(indexPath));
    }

    [Fact(DisplayName = "Interrupted BBQ v2 staged migration keeps the source and resumes with legacy bytes intact")]
    public void InterruptedV2MigrationAfterStagingPreservesSourceAndResumes()
    {
        string extracted = HistoricalIndexFixtures.Extract(
            HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-staged-recovery-{Guid.NewGuid():N}"));
        string indexPath = Path.Combine(extracted, "loose");
        long originalGeneration;
        string originalSegmentId;
        byte[] originalBody;
        ScoreSnapshot[] originalScores = CaptureSearchScores(indexPath);
        Dictionary<string, string> originalFiles = SnapshotFiles(indexPath);
        string[] originalFileNames = originalFiles.Keys.ToArray();

        using (var directory = new MMapDirectory(indexPath))
        {
            var commit = Assert.Single(IndexFileInspector.FindCommitFiles(indexPath));
            originalGeneration = commit.Generation;
            SegmentInfo segment = Assert.Single(IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos);
            originalSegmentId = segment.SegmentId;
            originalBody = ReadQuantisedBody(directory, segment);

            IndexCodecMigrationPlan plan = IndexCodecMigrator.Plan(directory);
            IndexCodecMigrationAction action = Assert.Single(plan.Actions,
                candidate => candidate.FormatId == "leancorpus.vectors.quantised");
            Assert.Equal((byte)2, action.FromVersion);
            Assert.Equal((byte)3, action.ToVersion);
            Assert.Equal(IndexCodecMigrationActionKind.Rewrite, action.Kind);

            string blockedCommit = Path.Combine(indexPath, $"segments_{originalGeneration + 1}");
            Directory.CreateDirectory(blockedCommit);
            IndexCodecMigrationResult failed = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = true,
                ValidateAfterMigration = false,
            });

            Assert.False(failed.Succeeded);
            Assert.Equal(IndexMigrationState.Failed, IndexMigrationRecovery.GetState(indexPath).State);
            Assert.Equal(originalGeneration, IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.Generation);
            Assert.NotNull(failed.StagingDirectory);
            Assert.True(Directory.Exists(failed.StagingDirectory));
            AssertStagedV2MigrationPayload(originalBody, failed.StagingDirectory);
            Dictionary<string, string> filesAfterFailure = SnapshotFiles(indexPath);
            foreach (string fileName in originalFileNames)
                Assert.Equal(originalFiles[fileName], filesAfterFailure[fileName]);

            Directory.Delete(blockedCommit);
            IndexCodecMigrationResult resumed = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = true,
            });
            Assert.True(resumed.Succeeded, string.Join("; ", resumed.Issues.Select(static issue => issue.Message)));
            Assert.Equal(IndexMigrationState.Published, IndexMigrationRecovery.GetState(indexPath).State);
        }

        using (var directory = new MMapDirectory(indexPath))
        {
            SegmentInfo migrated = Assert.Single(IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos);
            byte[] migratedBody = ReadQuantisedBody(directory, migrated, expectedVersion: 3);
            AssertV2MigrationBodyPreserved(originalBody, migratedBody);
            IndexCheckResult health = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
            Assert.True(health.IsHealthy,
                string.Join("; ", health.DetailedIssues.Select(static issue => $"{issue.Code}: {issue.Message}")));
        }

        Assert.Equal(originalScores, CaptureSearchScores(indexPath));
    }

    [Theory(DisplayName = "Legacy BBQ remains readable but cannot be opened for writing")]
    [InlineData(false)]
    [InlineData(true)]
    public void WriterOpenRejectsLegacyBbqWithoutMutatingTheIndex(bool migrateToTaggedV3)
    {
        string extracted = HistoricalIndexFixtures.Extract(
            HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-writer-guard-{migrateToTaggedV3}-{Guid.NewGuid():N}"));
        string indexPath = Path.Combine(extracted, "loose");
        if (migrateToTaggedV3)
        {
            using var migrationDirectory = new MMapDirectory(indexPath);
            IndexCodecMigrationResult migration = IndexCodecMigrator.Migrate(migrationDirectory,
                new IndexCodecMigrationOptions { DryRun = false, ValidateBeforeMigration = true, ValidateAfterMigration = true });
            Assert.True(migration.Succeeded, string.Join("; ", migration.Issues.Select(static issue => issue.Message)));
        }

        Dictionary<string, string> before = SnapshotFiles(indexPath);
        using var directory = new MMapDirectory(indexPath);
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            new IndexWriter(directory, new IndexWriterConfig { VectorQuantisation = VectorQuantisation.BBQ }));
        Assert.True(
            exception.Message.Contains("legacy BBQ", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("older codec files", StringComparison.OrdinalIgnoreCase),
            $"Writer-open rejection should identify the legacy BBQ limitation or required structural migration: {exception.Message}");
        Assert.Equal(before, SnapshotFiles(indexPath));

        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(
            new VectorQuery("embedding", CreateBasisVector(128, 0), topK: 5, efSearch: 128), topN: 5);
        ScoreDoc best = Assert.Single(results.ScoreDocs.Where(static hit => hit.DocId == 1));
        Assert.Equal(0.09225467f, best.Score, 5);
    }

    [Fact(DisplayName = "AddIndexes rejects legacy BBQ before flushing pending destination documents")]
    public void AddIndexesRejectsLegacyBbqBeforeDestinationMutation()
    {
        string extracted = HistoricalIndexFixtures.Extract(
            HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-import-guard-{Guid.NewGuid():N}"));
        string sourcePath = Path.Combine(extracted, "loose");
        string targetPath = Path.Combine(_fixture.Path, $"pre-v3-bbq-import-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetPath);

        using var source = new MMapDirectory(sourcePath);
        using var target = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(target, new IndexWriterConfig
        {
            MaxBufferedDocs = 8,
            VectorQuantisation = VectorQuantisation.BBQ,
        });
        var pending = new LeanDocument();
        pending.Add(new StringField("id", "pending"));
        writer.AddDocument(pending);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => writer.AddIndexes(source));
        Assert.Contains("legacy BBQ", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(targetPath, "seg_*.seg"));

        writer.Commit();
        using var searcher = new IndexSearcher(target);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "pending"), 10).TotalHits);
        Assert.Equal(0, searcher.Search(new VectorQuery("embedding", CreateBasisVector(128, 0), topK: 5), 5).TotalHits);
    }

    private static Dictionary<string, string> SnapshotFiles(string directory)
        => Directory.GetFiles(directory)
            .Order(StringComparer.Ordinal)
            .ToDictionary(static path => Path.GetFileName(path)!, static path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))), StringComparer.Ordinal);

    private static JsonElement ReadAndVerifyManifest(string extracted)
    {
        using Stream resource = typeof(PreV3BbqV2CompatibilityTests).Assembly.GetManifestResourceStream(
            "Rowles.LeanCorpus.Tests.Shared.Fixtures.Indexes.pre-v3-bbq-v2.manifest.json")
            ?? throw new InvalidDataException("Pinned pre-v3 BBQ manifest is missing.");
        using var bytes = new MemoryStream();
        resource.CopyTo(bytes);
        byte[] manifestBytes = bytes.ToArray();
        Assert.Equal(ManifestSha256, Convert.ToHexStringLower(SHA256.HashData(manifestBytes)));

        using JsonDocument document = JsonDocument.Parse(manifestBytes);
        JsonElement manifest = document.RootElement.Clone();
        Assert.Equal(1, manifest.GetProperty("FixtureFormatVersion").GetInt32());
        Assert.Equal("pre-v3-bbq-v2", manifest.GetProperty("FixtureId").GetString());
        Assert.Equal("fix/bbq-retrieval-quality", manifest.GetProperty("SourceBranch").GetString());
        Assert.Equal("3dab5b656c0077470cd216646ba085a04fa40ef3", manifest.GetProperty("SourceCommit").GetString());
        Assert.Equal(311031, manifest.GetProperty("GenerationSeed").GetInt32());
        Assert.Equal("embedding", manifest.GetProperty("FieldName").GetString());
        Assert.Equal(128, manifest.GetProperty("Dimension").GetInt32());
        Assert.Equal(2, manifest.GetProperty("QuantisedVectorBodyVersion").GetInt32());
        Assert.Equal(new[] { 0, 1, 2, 4, 6 },
            manifest.GetProperty("PresentVectorDocumentIds").EnumerateArray().Select(static value => value.GetInt32()));
        Assert.Equal(new[] { 0 },
            manifest.GetProperty("ExplicitZeroVectorDocumentIds").EnumerateArray().Select(static value => value.GetInt32()));
        Assert.Equal(new[] { 3, 5, 7 },
            manifest.GetProperty("MissingVectorDocumentIds").EnumerateArray().Select(static value => value.GetInt32()));
        Assert.Equal("189d29dbe1fe84cbd5806ab56c32babcada457efdb1c3ff5da9f6530b11f2b9c",
            manifest.GetProperty("ArchiveSha256").GetString());
        Assert.Equal(new[] { "compound", "loose" },
            manifest.GetProperty("Layouts").EnumerateArray().Select(static value => value.GetString()).Order(StringComparer.Ordinal));
        return manifest;
    }

    private static void VerifySnapshotFiles(JsonElement manifest, string extracted)
    {
        var expectedFiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement file in manifest.GetProperty("Files").EnumerateArray())
        {
            string name = file.GetProperty("Name").GetString()!;
            Assert.True(expectedFiles.Add(name), $"Duplicate fixture manifest path '{name}'.");
            string path = Path.Combine(extracted, name.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"Fixture file '{name}' is missing.");
            Assert.Equal(file.GetProperty("Length").GetInt64(), new FileInfo(path).Length);
            using Stream stream = File.OpenRead(path);
            Assert.Equal(file.GetProperty("Sha256").GetString(), Convert.ToHexStringLower(SHA256.HashData(stream)));
        }

        string[] actualFiles = Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(extracted, path).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();
        Assert.Equal(expectedFiles.Count, actualFiles.Length);
        Assert.All(actualFiles, path => Assert.Contains(path, expectedFiles));
    }

    private static void AssertLegacyV2Body(string indexPath, string segmentId)
    {
        string vectorPath = VectorFilePaths.QuantisedVectorFile(Path.Combine(indexPath, segmentId), "embedding");
        using (var input = new IndexInput(vectorPath))
        using (var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.vectors.quantised")))
            Assert.Equal(2, frame.Metadata.FormatVersion);

        using var reader = QuantisedVectorReader.Open(vectorPath);
        Assert.Equal(VectorQuantisation.BBQ, reader.Quantisation);
        Assert.Equal(128, reader.Dimension);
        Assert.True(reader.HasVector(0));
        Assert.False(reader.HasVector(3));
        Assert.Equal(0.026637087f, reader.Centroid[0], 7);
        Assert.Equal(Convert.FromHexString("91244992244992244992244992244992"), reader.GetRawBBQVector(1).ToArray());

        var corrections = reader.GetBBQCorrections(1);
        Assert.Equal(-0.0523866f, corrections.C1, 6);
        Assert.Equal(3.1178696f, corrections.C2, 6);
        Assert.Equal(1.0213984f, corrections.C3, 6);

        float[] decoded = reader.ReadVector(1);
        Assert.Equal(reader.Centroid[0] + 1f, decoded[0]);
        Assert.Equal(reader.Centroid[1] - 1f, decoded[1]);
    }

    private static byte[] ReadQuantisedBody(MMapDirectory directory, SegmentInfo segment, int? expectedVersion = null)
    {
        string name = VectorFilePaths.QuantisedVectorFile(segment.SegmentId, "embedding");
        using ISegmentFileSource files = segment.IsCompoundFile
            ? new CompoundSegmentFileSource(directory, segment.SegmentId)
            : new LooseSegmentFileSource(directory, segment.SegmentId);
        using var input = files.OpenInput(name);
        using var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Quantised);
        if (expectedVersion is int version)
            Assert.Equal(version, frame.FormatVersion);
        return frame.ReadBody();
    }

    private static void AssertStagedV2MigrationPayload(byte[] originalBody, string stagingDirectory)
    {
        const int commonMetadataLength = sizeof(int) + sizeof(int) + sizeof(byte) + 1;
        var stagedBodies = new List<byte[]>();
        foreach (string path in Directory.GetFiles(stagingDirectory, "*.vq", SearchOption.AllDirectories))
        {
            using var input = new IndexInput(path);
            using var frame = CodecFileReader.OpenSupported(input, VectorCodecFiles.Quantised);
            if (frame.FormatVersion == 3)
                stagedBodies.Add(frame.ReadBody());
        }

        Assert.NotEmpty(stagedBodies);
        foreach (byte[] body in stagedBodies)
            AssertV2MigrationBodyPreserved(originalBody, body);
        Assert.Equal((byte)2, stagedBodies[0][commonMetadataLength]);
    }

    private static void AssertV2MigrationBodyPreserved(byte[] originalBody, byte[] migratedBody)
    {
        const int commonMetadataLength = sizeof(int) + sizeof(int) + sizeof(byte) + 1;
        Assert.Equal(originalBody.AsSpan(0, commonMetadataLength).ToArray(),
            migratedBody.AsSpan(0, commonMetadataLength).ToArray());
        Assert.Equal((byte)2, migratedBody[commonMetadataLength]); // LegacyBbqSign
        Assert.Equal(originalBody.AsSpan(commonMetadataLength).ToArray(),
            migratedBody.AsSpan(commonMetadataLength + 1).ToArray());
    }

    private static ScoreSnapshot[] CaptureSearchScores(string indexPath)
    {
        using var searcher = new IndexSearcher(new MMapDirectory(indexPath));
        return searcher.Search(
                new VectorQuery("embedding", CreateBasisVector(128, 0), topK: 5, efSearch: 128),
                topN: 5)
            .ScoreDocs
            .Select(static hit => new ScoreSnapshot(hit.DocId, hit.Score))
            .ToArray();
    }

    private static float[] CreateBasisVector(int dimension, int index)
    {
        var vector = new float[dimension];
        vector[index] = 1f;
        return vector;
    }

    private readonly record struct ScoreSnapshot(int DocId, float Score);
}
