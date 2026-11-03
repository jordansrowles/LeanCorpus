using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Hnsw;
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

    [Theory(DisplayName = "BBQ v2 migration is refused without changing historical bytes or scores")]
    [InlineData("loose")]
    [InlineData("compound")]
    public void V2MigrationIsReadOnly(string layout)
    {
        string extracted = HistoricalIndexFixtures.Extract(HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-migration-{layout}-{Guid.NewGuid():N}"));
        string path = Path.Combine(extracted, layout);
        var before = SnapshotFiles(path);
        var scores = CaptureSearchScores(path);
        using var directory = new MMapDirectory(path);
        Assert.False(IndexCodecMigrator.Plan(directory).CanExecute);
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == IndexCheckIssueCodes.ReadOnlyRetiredCodec);
        Assert.Null(result.StagingDirectory);
        Assert.Equal(before, SnapshotFiles(path));
        Assert.Equal(scores, CaptureSearchScores(path));
    }

    [Theory(DisplayName = "Old BBQ migration markers cannot resume publication")]
    [InlineData(IndexMigrationState.Failed)]
    [InlineData(IndexMigrationState.ReadyToPublish)]
    public void OldMigrationMarkerCannotResume(IndexMigrationState state)
    {
        string extracted = HistoricalIndexFixtures.Extract(HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-resume-{state}-{Guid.NewGuid():N}"));
        string path = Path.Combine(extracted, "loose");
        string staging = Path.Combine(extracted, "staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "preserve.tmp"), "historical staging evidence");
        using var directory = new MMapDirectory(path);
        var plan = IndexCodecMigrator.Plan(directory);
        IndexMigrationRecovery.WriteMarker(path, new IndexMigrationMarker
        {
            State = state,
            SourceDirectory = path,
            StagingDirectory = staging,
            SourceCommitGeneration = checked((int)IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.Generation),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            PlannedActions = plan.Actions,
        }, durable: false);
        var before = SnapshotFiles(path);
        var stagedBefore = SnapshotFiles(staging);
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == IndexCheckIssueCodes.ReadOnlyRetiredCodec);
        Assert.Equal(before, SnapshotFiles(path));
        Assert.Equal(stagedBefore, SnapshotFiles(staging));
        Assert.Equal(state, IndexMigrationRecovery.GetState(path).State);
    }

    [Theory(DisplayName = "Legacy BBQ remains readable but cannot be opened for writing")]
    [InlineData("loose")]
    [InlineData("compound")]
    public void WriterOpenRejectsLegacyBbqWithoutMutatingTheIndex(string layout)
    {
        string extracted = HistoricalIndexFixtures.Extract(HistoricalIndexFixture.PreV3BbqV2,
            Path.Combine(_fixture.Path, $"pre-v3-bbq-writer-guard-{layout}-{Guid.NewGuid():N}"));
        string indexPath = Path.Combine(extracted, layout);
        var before = SnapshotFiles(indexPath);
        using var directory = new MMapDirectory(indexPath);
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new IndexWriter(directory, new IndexWriterConfig()));
        Assert.Contains("CodecReadOnly", exception.Message);
        Assert.Equal(before, SnapshotFiles(indexPath));
        Assert.Equal(0.09225467f, Assert.Single(CaptureSearchScores(indexPath).Where(hit => hit.DocId == 1)).Score, 5);
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
            VectorQuantisation = VectorQuantisation.Int8,
        });
        var pending = new LeanDocument();
        pending.Add(new StringField("id", "pending"));
        writer.AddDocument(pending);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => writer.AddIndexes(source));
        Assert.Contains("CodecReadOnly", exception.Message);
        Assert.Empty(Directory.GetFiles(targetPath, "seg_*.seg"));

        writer.Commit();
        using var searcher = new IndexSearcher(target);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "pending"), 10).TotalHits);
        Assert.Equal(0, searcher.Search(new VectorQuery("embedding", CreateBasisVector(128, 0), topK: 5), 5).TotalHits);
    }

    [Fact(DisplayName = "Migrated v3 sign-BBQ tag 2 remains readable and refuses writes and migration")]
    public void V3LegacySignTagRemainsReadableAndReadOnly()
    {
        string indexPath = HistoricalBbqIndexFixtures.CopyLegacySignV3To(
            Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-{Guid.NewGuid():N}"));
        var before = SnapshotFiles(indexPath);
        using var directory = new MMapDirectory(indexPath);

        var inventory = IndexFormatInspector.Inspect(directory, new IndexFormatInspectionOptions { IncludeChecksums = true });
        Assert.DoesNotContain(inventory.Issues, issue => issue.Severity == IndexCheckSeverity.Error);
        Assert.Equal(3, Assert.Single(inventory.Segments)
            .Files.Single(file => file.FileName.EndsWith(".vq", StringComparison.Ordinal)).FormatVersion);

        var compatibility = IndexCompatibility.Check(directory);
        Assert.True(compatibility.CanRead);
        Assert.True(compatibility.CanValidate);
        Assert.False(compatibility.CanWrite);
        Assert.False(compatibility.CanMigrate);
        Assert.False(compatibility.RequiresMigration);
        Assert.False(compatibility.MustReject);

        var committed = Assert.Single(IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos);
        using var vectors = QuantisedVectorReader.Open(
            VectorFilePaths.QuantisedVectorFile(Path.Combine(indexPath, committed.SegmentId), "embedding"));
        Assert.Equal(QuantisedVectorStorageKind.LegacyBbqSign, vectors.StorageKind);
        Assert.Equal(8, vectors.DocCount);
        Assert.Equal(128, vectors.Dimension);

        var validation = IndexValidator.Check(directory, new IndexCheckOptions { Deep = true });
        Assert.True(validation.IsHealthy,
            string.Join("; ", validation.DetailedIssues.Select(issue => $"{issue.Code}: {issue.Message}")));
        ScoreSnapshot[] scores = CaptureSearchScores(indexPath);
        Assert.Equal(new[] { 1, 6, 2, 0, 4 }, scores.Select(static score => score.DocId));
        uint[] expectedScoreBits = [0x3DBCF005, 0x3DB73B95, 0xBDB2AC0A, 0xBDB335DA, 0xBDB335DA];
        for (int index = 0; index < scores.Length; index++)
        {
            float capturedScore = BitConverter.UInt32BitsToSingle(expectedScoreBits[index]);
            Assert.InRange(MathF.Abs(scores[index].Score - capturedScore), 0f, 1e-6f);
        }

        InvalidOperationException writeException = Assert.Throws<InvalidOperationException>(() =>
            new IndexWriter(directory, new IndexWriterConfig()));
        Assert.Contains("CodecReadOnly", writeException.Message, StringComparison.Ordinal);
        Assert.Equal(before, SnapshotFiles(indexPath));
        string lockPath = Path.Combine(indexPath, "write.lock");
        using (File.Open(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        File.Delete(lockPath);

        Assert.False(IndexCodecMigrator.Plan(directory).CanExecute);
        string staging = Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-stage-{Guid.NewGuid():N}");
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            StagingDirectory = staging,
        });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == IndexCheckIssueCodes.ReadOnlyRetiredCodec);
        Assert.False(Directory.Exists(staging));
        Assert.Equal(before, SnapshotFiles(indexPath));
        Assert.Equal(scores, CaptureSearchScores(indexPath));
    }

    [Fact(DisplayName = "Migrated v3 sign-BBQ tag 2 is refused by import before destination mutation")]
    public void V3LegacySignTagImportIsRefusedBeforeDestinationMutation()
    {
        string sourcePath = HistoricalBbqIndexFixtures.CopyLegacySignV3To(
            Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-import-{Guid.NewGuid():N}"));
        var sourceBefore = SnapshotFiles(sourcePath);
        string targetPath = Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-import-target-{Guid.NewGuid():N}");
        using var source = new MMapDirectory(sourcePath);
        using var target = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(target, new IndexWriterConfig
        {
            MaxBufferedDocs = 8,
            VectorQuantisation = VectorQuantisation.Int8,
        });
        var pending = new LeanDocument();
        pending.Add(new StringField("id", "pending"));
        writer.AddDocument(pending);
        var targetBefore = SnapshotFiles(targetPath);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => writer.AddIndexes(source));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, SnapshotFiles(sourcePath));
        Assert.Equal(targetBefore, SnapshotFiles(targetPath));
    }

    [Fact(DisplayName = "Migrated v3 sign-BBQ tag 2 is refused by merge before destination allocation")]
    public void V3LegacySignTagMergeIsRefusedBeforeDestinationAllocation()
    {
        string sourcePath = HistoricalBbqIndexFixtures.CopyLegacySignV3To(
            Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-merge-{Guid.NewGuid():N}"));
        using var source = new MMapDirectory(sourcePath);
        var segments = IndexRecovery.RecoverLatestCommit(sourcePath, cleanupOrphans: false)!.SegmentInfos;
        string targetPath = Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-merge-target-{Guid.NewGuid():N}");
        using var target = new MMapDirectory(targetPath);
        var merger = new SegmentMerger(target, NoMergePolicy.Instance);
        int next = 42;

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            merger.MergeSegmentsFromDirectory(source, segments, ref next, new IndexWriterConfig()));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(42, next);
        Assert.Empty(Directory.EnumerateFileSystemEntries(targetPath));
    }

    [Fact(DisplayName = "Migrated v3 sign-BBQ tag 2 refuses graph writes and pending recovery")]
    public void V3LegacySignTagGraphWriteAndPendingRecoveryAreRefused()
    {
        string indexPath = HistoricalBbqIndexFixtures.CopyLegacySignV3To(
            Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-recovery-{Guid.NewGuid():N}"));
        using var directory = new MMapDirectory(indexPath);
        SegmentInfo segment = Assert.Single(IndexRecovery.RecoverLatestCommit(indexPath, cleanupOrphans: false)!.SegmentInfos);
        using var reader = new SegmentReader(directory, segment);
        var graph = reader.GetHnswGraph("embedding")!;
        string graphPath = Path.Combine(_fixture.Path, $"pre-retirement-v3-sign-{Guid.NewGuid():N}.hnsw");

        InvalidOperationException graphException = Assert.Throws<InvalidOperationException>(() =>
            HnswWriter.Write(graphPath, graph, 128, normalised: true));

        Assert.Contains("CodecReadOnly", graphException.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(graphPath));
        string commitPath = Assert.Single(Directory.GetFiles(indexPath, "segments_*"));
        File.Move(commitPath, commitPath + ".pending");
        var pendingBefore = SnapshotFiles(indexPath);

        InvalidOperationException recoveryException = Assert.Throws<InvalidOperationException>(() =>
            IndexRecovery.RecoverLatestCommit(indexPath));

        Assert.Contains("CodecReadOnly", recoveryException.Message, StringComparison.Ordinal);
        Assert.Equal(pendingBefore, SnapshotFiles(indexPath));
    }

    private static Dictionary<string, string> SnapshotFiles(string directory)
        => Directory.GetFiles(directory)
            .Where(static path => Path.GetFileName(path) != "write.lock")
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
