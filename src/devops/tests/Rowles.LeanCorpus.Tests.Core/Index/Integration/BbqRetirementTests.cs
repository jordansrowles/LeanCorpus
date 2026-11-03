using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core.Index.Integration;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.CodecKit)]
public sealed class BbqRetirementTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public BbqRetirementTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Theory]
    [InlineData("loose")]
    [InlineData("compound")]
    [InlineData("mixed")]
    [InlineData("all-deleted")]
    public void HistoricalV3_SearchRetainsCapturedScoresAndPresence(string scenario)
    {
        string root = Extract();
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
        foreach (var file in manifest.RootElement.GetProperty("files").EnumerateArray())
            Assert.Equal(file.GetProperty("sha256").GetString(),
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, file.GetProperty("path").GetString()!)))));

        float[] query = manifest.RootElement.GetProperty("query").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var expected = manifest.RootElement.GetProperty("captures").EnumerateArray()
            .Single(value => value.GetProperty("scenario").GetString() == scenario);
        using var directory = new MMapDirectory(Path.Combine(root, scenario));
        using var searcher = new IndexSearcher(directory);
        var actual = searcher.Search(new VectorQuery("embedding", query, topK: 5), 5, TestContext.Current.CancellationToken);

        Assert.Equal(expected.GetProperty("TotalHits").GetInt64(), actual.TotalHits);
        Assert.Equal(expected.GetProperty("scores").EnumerateArray().Select(value => value.GetProperty("DocId").GetInt32()),
            actual.ScoreDocs.Select(value => value.DocId));
        Assert.Equal(expected.GetProperty("scores").EnumerateArray().Select(value => value.GetProperty("Score").GetSingle()),
            actual.ScoreDocs.Select(value => value.Score));
    }

    [Theory]
    [InlineData("loose")]
    [InlineData("compound")]
    [InlineData("mixed")]
    [InlineData("all-deleted")]
    public void WriterOpen_RejectsCommittedBbqBeforeRecoveryChangesFiles(string scenario)
    {
        string path = Path.Combine(Extract(), scenario);
        File.WriteAllText(Path.Combine(path, "segments_987.pending"), "pending state must remain untouched");
        var before = Snapshot(path);
        using var directory = new MMapDirectory(path);

        var exception = Assert.Throws<InvalidOperationException>(() => new IndexWriter(directory, new IndexWriterConfig()));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(path));
        string lockPath = Path.Combine(path, "write.lock");
        using (File.Open(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        File.Delete(lockPath);
    }

    [Theory]
    [InlineData(1, "LegacyBbqSign")]
    [InlineData(2, "LegacyBbqSign")]
    [InlineData(3, "LegacyBbqSign")]
    [InlineData(3, "BbqOsqCanonicalInt8")]
    public void Catalogue_BbqIsReadableButRetiredForEveryMutation(int version, string representation)
    {
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.vectors.quantised");
        var source = new CodecRepresentationIdentity(descriptor.FormatId, version, representation);
        var target = new CodecRepresentationIdentity(descriptor.FormatId, 3, "LegacyInt8");

        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Read, source).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Create, target: source with { Version = 3 }).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.RewriteSource, source).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Migrate, source, target).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Migrate, source, source).IsAllowed);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Catalogue_Int8PreservesExplicitStructuralMigrationRoutes(int version)
    {
        var descriptor = CodecCatalog.Default.GetFile("leancorpus.vectors.quantised");
        var source = new CodecRepresentationIdentity(descriptor.FormatId, version, "LegacyInt8");
        var target = source with { Version = 3 };

        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Read, source).IsAllowed);
        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.RewriteSource, source).IsAllowed);
        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Create, target: target).IsAllowed);
        Assert.True(descriptor.Evaluate(CodecLifecycleOperation.Migrate, source, target).IsAllowed);
        Assert.False(descriptor.Evaluate(CodecLifecycleOperation.Read, source with { RepresentationId = "unknown" }).IsAllowed);
    }

    [Fact]
    public void Migration_CurrentInt8V3IsAValidNoOp()
    {
        string path = Path.Combine(_fixture.Path, $"current-int8-no-op-{Guid.NewGuid():N}");
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            VectorQuantisation = VectorQuantisation.Int8,
            BuildHnswOnFlush = true,
            HnswSeed = 739391L,
        }))
        {
            for (int docId = 0; docId < 2; docId++)
            {
                var document = new LeanDocument();
                document.Add(new StringField("id", $"int8-{docId}"));
                document.Add(new VectorField("embedding", Enumerable.Range(0, 128)
                    .Select(index => (float)((index + docId) % 7 - 3)).ToArray()));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        var before = Snapshot(path);
        var plan = IndexCodecMigrator.Plan(directory);
        Assert.True(plan.CanExecute, string.Join("; ", plan.Issues.Select(static issue => issue.Message)));
        Assert.All(plan.Actions, action => Assert.Equal(IndexCodecMigrationActionKind.NoOp, action.Kind));
        Assert.DoesNotContain(plan.Issues, issue => issue.Code == IndexCheckIssueCodes.ReadOnlyRetiredCodec);

        string staging = Path.Combine(_fixture.Path, $"current-int8-no-op-stage-{Guid.NewGuid():N}");
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions
        {
            DryRun = false,
            StagingDirectory = staging,
        });

        Assert.True(result.Succeeded, string.Join("; ", result.Issues.Select(static issue => issue.Message)));
        Assert.False(Directory.Exists(staging));
        Assert.Equal(before, Snapshot(path));
        using var searcher = new IndexSearcher(directory);
        Assert.Equal(2, searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    private string Extract() => HistoricalIndexFixtures.Extract(HistoricalIndexFixture.ExperimentalBbqV3,
        Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N")));

    [Fact]
    public void GraphWriter_RejectsHistoricalBbqBeforeCreatingOutput()
    {
        string path = Path.Combine(Extract(), "loose");
        using var directory = new MMapDirectory(path);
        var info = Assert.Single(IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos);
        using var reader = new SegmentReader(directory, info);
        var graph = reader.GetHnswGraph("embedding")!;
        string destination = Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N") + ".hnsw");

        var exception = Assert.Throws<InvalidOperationException>(() => HnswWriter.Write(destination, graph, 128, normalised: true));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(destination));
    }

    [Theory]
    [InlineData("loose")]
    [InlineData("compound")]
    [InlineData("mixed")]
    [InlineData("all-deleted")]
    [InlineData("legacy-loose")]
    [InlineData("legacy-compound")]
    public void Compatibility_ReportsReadableRetirementWithoutSuggestingMigration(string scenario)
    {
        string path = scenario.StartsWith("legacy-", StringComparison.Ordinal)
            ? Path.Combine(HistoricalIndexFixtures.Extract(HistoricalIndexFixture.PreV3BbqV2,
                Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"))), scenario["legacy-".Length..])
            : Path.Combine(Extract(), scenario);
        using var directory = new MMapDirectory(path);

        var result = IndexCompatibility.Check(directory);

        Assert.True(result.CanRead);
        Assert.False(result.CanWrite);
        Assert.False(result.CanMigrate);
        Assert.False(result.RequiresMigration);
        Assert.False(result.MustReject);
        Assert.Contains(result.Issues, issue => issue.Code == IndexCheckIssueCodes.ReadOnlyRetiredCodec);
    }

    [Fact]
    public void PendingBbqCommit_IsNotPromotedByWriterRecovery()
    {
        string path = Path.Combine(Extract(), "loose");
        string commit = Assert.Single(Directory.EnumerateFiles(path, "segments_*"));
        File.Move(commit, commit + ".pending");
        var before = Snapshot(path);
        using var directory = new MMapDirectory(path);

        var exception = Assert.Throws<InvalidOperationException>(() => new IndexWriter(directory, new IndexWriterConfig()));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(path));
    }

    [Fact]
    public void WriterOpen_GuardsPhysicalQuantisedFilesWhenSegmentMetadataOmitsThem()
    {
        string path = Path.Combine(Extract(), "loose");
        using var directory = new MMapDirectory(path);
        SegmentInfo segment = Assert.Single(IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentInfos);
        segment.VectorFields.Clear();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            VectorMutationGuard.EnsureCanRewrite(directory, [segment]));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitRecovery_DoesNotPublishAnUncommittedBbqGeneration()
    {
        string path = Path.Combine(Extract(), "loose");
        string commit = Assert.Single(Directory.EnumerateFiles(path, "segments_*"));
        File.Move(commit, commit + ".pending");
        var before = Snapshot(path);

        var exception = Assert.Throws<InvalidOperationException>(() => IndexRecovery.RecoverLatestCommit(path));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(path));
    }

    [Theory]
    [InlineData("loose")]
    [InlineData("compound")]
    [InlineData("mixed")]
    [InlineData("all-deleted")]
    public void ExplicitRecovery_CanRestoreAlreadyCommittedHistoricalBbq(string scenario)
    {
        string path = Path.Combine(Extract(), scenario);
        var before = Snapshot(path);

        var committed = IndexRecovery.RecoverLatestCommit(path);

        Assert.NotNull(committed);
        Assert.Equal(before, Snapshot(path));
    }

    [Fact]
    public void ExplicitRecovery_CannotReplaceACommittedBbqIndexWithPendingInt8()
    {
        string path = Path.Combine(Extract(), "mixed");
        var committed = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!;
        var int8 = Assert.Single(committed.SegmentInfos.Where(segment => segment.VectorFields.Any(field => field.Quantisation == VectorQuantisation.Int8)));
        WritePending(path, committed.Generation + 1, [int8]);
        var before = Snapshot(path);

        Assert.Throws<InvalidOperationException>(() => IndexRecovery.RecoverLatestCommit(path));

        Assert.Equal(before, Snapshot(path));
    }

    [Fact]
    public void WriterRecovery_PreservesSupportedInt8PendingCommitAfterCorruptPreviousCommit()
    {
        string path = Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig { VectorQuantisation = VectorQuantisation.Int8 }))
        {
            var document = new LeanDocument();
            document.Add(new StringField("id", "survivor"));
            document.Add(new VectorField("embedding", new float[] { 1, 0 }));
            writer.AddDocument(document);
            writer.Commit();
        }
        var committed = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!;
        WritePending(path, committed.Generation + 1, committed.SegmentInfos);
        File.WriteAllText(committed.CommitFilePath, "corrupt previous commit");

        using (var writer = new IndexWriter(directory, new IndexWriterConfig { VectorQuantisation = VectorQuantisation.Int8 })) { }

        using var searcher = new IndexSearcher(directory);
        Assert.Single(searcher.Search(new TermQuery("id", "survivor"), 1, TestContext.Current.CancellationToken).ScoreDocs);
        Assert.False(File.Exists(Path.Combine(path, $"segments_{committed.Generation + 1}.pending")));
    }

    private static void WritePending(string path, int generation, IReadOnlyList<SegmentInfo> segments)
    {
        var pending = new CommitData
        {
            Generation = generation,
            Segments = segments.Select(segment => segment.SegmentId).ToList(),
            SegmentStates = segments.Select(SegmentCommitState.FromSegmentInfo).ToList(),
        };
        File.WriteAllText(Path.Combine(path, $"segments_{generation}.pending"),
            CommitFileFormat.Wrap(JsonSerializer.Serialize(pending, LeanCorpusJsonContext.Default.CommitData)));
    }

    [Fact]
    public void ProductCli_ReportsReadableRetirementAndRefusesMigration()
    {
        string path = Path.Combine(Extract(), "loose");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int code = Rowles.LeanCorpus.Cli.IndexCheckerCli.Run(["compat", path, "--json"], output, error);

        Assert.Equal(0, code);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("ReadOnlyRetired", json.RootElement.GetProperty("status").GetString());
        Assert.True(json.RootElement.GetProperty("canRead").GetBoolean());
        Assert.False(json.RootElement.GetProperty("canWrite").GetBoolean());
        Assert.False(json.RootElement.GetProperty("canMigrate").GetBoolean());
        Assert.Equal(string.Empty, error.ToString());
        var before = Snapshot(path);
        output.GetStringBuilder().Clear();
        int migrationCode = Rowles.LeanCorpus.Cli.IndexCheckerCli.Run(["migrate", path, "--execute", "--json"], output, error);
        Assert.NotEqual(0, migrationCode);
        Assert.Contains("ReadOnlyRetired", output.ToString() + error, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(path));
    }

    [Theory]
    [InlineData("loose")]
    [InlineData("compound")]
    [InlineData("mixed")]
    [InlineData("all-deleted")]
    public void Migration_RejectsRetiredIndexIncludingCurrentNoOpBeforeStaging(string scenario)
    {
        string path = Path.Combine(Extract(), scenario);
        string staging = Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        var before = Snapshot(path);
        using var directory = new MMapDirectory(path);

        var plan = IndexCodecMigrator.Plan(directory);
        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Issues, issue => issue.Message.Contains("ReadOnlyRetired", StringComparison.Ordinal));
        var result = IndexCodecMigrator.Migrate(directory, new IndexCodecMigrationOptions { DryRun = false, StagingDirectory = staging });
        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Message.Contains("ReadOnlyRetired", StringComparison.Ordinal));
        Assert.Equal(before, Snapshot(path));
        Assert.False(Directory.Exists(staging));
    }

    [Theory]
    [InlineData("loose")]
    [InlineData("compound")]
    [InlineData("mixed")]
    [InlineData("all-deleted")]
    public void Import_RejectsEveryRetiredSourceBeforeFlushingDestination(string scenario)
    {
        string sourcePath = Path.Combine(Extract(), scenario);
        var sourceBefore = Snapshot(sourcePath);
        string targetPath = Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        using var source = new MMapDirectory(sourcePath);
        using var target = new MMapDirectory(targetPath);
        using var writer = new IndexWriter(target, new IndexWriterConfig());
        var document = new LeanDocument();
        document.Add(new StringField("id", "buffered"));
        writer.AddDocument(document);
        var targetBefore = Snapshot(targetPath);

        var exception = Assert.Throws<InvalidOperationException>(() => writer.AddIndexes(source));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, Snapshot(sourcePath));
        Assert.Equal(targetBefore, Snapshot(targetPath));
        writer.Commit();
        using var searcher = new IndexSearcher(target);
        Assert.Single(searcher.Search(new TermQuery("id", "buffered"), 10, TestContext.Current.CancellationToken).ScoreDocs);
    }

    [Theory]
    [InlineData("loose")]
    [InlineData("compound")]
    [InlineData("mixed")]
    [InlineData("all-deleted")]
    public void Merge_RejectsRetiredSourceBeforeAllocatingOutputOrdinal(string scenario)
    {
        string sourcePath = Path.Combine(Extract(), scenario);
        using var source = new MMapDirectory(sourcePath);
        var segments = IndexRecovery.RecoverLatestCommit(sourcePath, cleanupOrphans: false)!.SegmentInfos;
        string targetPath = Path.Combine(_fixture.Path, Guid.NewGuid().ToString("N"));
        using var target = new MMapDirectory(targetPath);
        var merger = new SegmentMerger(target, NoMergePolicy.Instance);
        int next = 42;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            merger.MergeSegmentsFromDirectory(source, segments, ref next, new IndexWriterConfig()));

        Assert.Contains("CodecReadOnly", exception.Message, StringComparison.Ordinal);
        Assert.Equal(42, next);
        Assert.Empty(Directory.EnumerateFileSystemEntries(targetPath));
    }

    private static string[] Snapshot(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
        .Where(file => Path.GetFileName(file) != "write.lock")
        .Order(StringComparer.Ordinal).Select(file => Path.GetRelativePath(path, file) + ":" +
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))).ToArray();
}
