namespace Rowles.LeanCorpus.Tests.Core.Index.Migration;

/// <summary>
/// Integration tests for <see cref="IndexCodecMigrator"/> resilience: family-coordinator
/// failure, mid-migration partial failure, interrupted-migration resume, and recovery-marker
/// edge states. These tests assert that a failed or interrupted migration leaves the source
/// index untouched and recoverable.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.CodecKit)]
public sealed class IndexCodecMigratorResilienceTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCodecMigratorResilienceTests(TestDirectoryFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════
    //  Coordination failure
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Throwing family coordinator leaves the source intact and marks Failed")]
    public void Migrate_CoordinatorThrows_LeavesSourceIntactAndMarksFailed()
    {
        var path = CreateCurrentVersionIndex("resilience_coord_throws");
        var segmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds.Single();
        var catalog = BuildThrowingCoordinatedCatalog();
        WriteLegacyEnvelope(Path.Combine(path, segmentId + ".ca"), 1, [10]);
        WriteLegacyEnvelope(Path.Combine(path, segmentId + ".cb"), 1, [20]);

        var sourceBefore = SnapshotSourceFiles(path);
        var originalCommit = IndexFileInspector.FindCommitFiles(path)[0];

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                Catalog = catalog,
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Issues, issue => issue.Code == IndexCheckIssueCodes.UnsupportedMigrationPath);
        Assert.Equal(IndexMigrationState.Failed, IndexMigrationRecovery.GetState(path).State);
        Assert.Equal(originalCommit.Generation, IndexFileInspector.FindCommitFiles(path)[0].Generation);
        Assert.Equal(sourceBefore, SnapshotSourceFilesWithoutMarker(path));
        Assert.NotNull(result.StagingDirectory);
        Assert.True(Directory.Exists(result.StagingDirectory));
    }

    [Fact(DisplayName = "Migrate: Throwing rewrite handler mid-sequence leaves the source intact and recoverable")]
    public void Migrate_RewriteHandlerThrows_PartialFailureLeavesSourceIntact()
    {
        var path = CreateCurrentVersionIndex("resilience_partial");
        DowngradeVersionByte(path, "*.fln", 0);

        var handlerDescriptor = CreateHandlerDescriptor(
            "unit-test.handler.throw", "unit-test.handler.family", ".zzfail",
            new ThrowingMigrationHandler());
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor("unit-test.handler.family", "handler family", [handlerDescriptor]))
            .Build();
        WriteLegacyEnvelope(Path.Combine(path, "orphan.zzfail"), 1, [42]);

        var sourceBefore = SnapshotSourceFiles(path);
        var originalCommit = IndexFileInspector.FindCommitFiles(path)[0];

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                Catalog = catalog,
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.False(result.Succeeded);
        // The segment-file .fln Reframe executed before the orphan handler threw, so it is
        // recorded in ExecutedActions while the .zzfail action is not.
        Assert.Contains(result.ExecutedActions, action => action.FileName!.EndsWith(".fln", StringComparison.Ordinal));
        Assert.DoesNotContain(result.ExecutedActions, action => action.FileName!.EndsWith(".zzfail", StringComparison.Ordinal));
        Assert.Equal(IndexMigrationState.Failed, IndexMigrationRecovery.GetState(path).State);
        Assert.Equal(originalCommit.Generation, IndexFileInspector.FindCommitFiles(path)[0].Generation);
        Assert.Equal(sourceBefore, SnapshotSourceFilesWithoutMarker(path));
        Assert.NotNull(result.StagingDirectory);
        Assert.True(Directory.Exists(result.StagingDirectory));

        // Failed marker plus preserved staging blocks rollback; abandon preserves staging.
        Assert.Throws<InvalidOperationException>(() => IndexMigrationRecovery.RollBack(path));
        IndexMigrationRecovery.Abandon(path);
        Assert.False(File.Exists(Path.Combine(path, IndexMigrationRecovery.MarkerFileName)));
        Assert.True(Directory.Exists(result.StagingDirectory));
    }

    [Fact(DisplayName = "Migrate: Descriptor migration handler rewrites a legacy file")]
    public void Migrate_DescriptorMigrationHandler_RewritesFile()
    {
        var path = CreateCurrentVersionIndex("resilience_handler_ok");
        var segmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds.Single();

        var handlerDescriptor = CreateHandlerDescriptor(
            "unit-test.handler.inc", "unit-test.handler.family", ".hnd",
            new IncrementHandler());
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor("unit-test.handler.family", "handler family", [handlerDescriptor]))
            .Build();
        WriteLegacyEnvelope(Path.Combine(path, segmentId + ".hnd"), 1, [10]);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                Catalog = catalog,
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = true,
            });

        Assert.True(result.Succeeded,
            string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        var migratedSegmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds.Single();
        Assert.Equal((byte)11, ReadSingleBodyByte(Path.Combine(path, migratedSegmentId + ".hnd"), handlerDescriptor));
    }

    // ═══════════════════════════════════════════════════
    //  Interrupted-migration resume
    // ═══════════════════════════════════════════════════

    [Theory(DisplayName = "Migrate: Resume recovers from a stale marker")]
    [InlineData(IndexMigrationState.Prepared)]
    [InlineData(IndexMigrationState.InProgress)]
    [InlineData(IndexMigrationState.Failed)]
    public void Migrate_Resume_StaleMarker_RecoversAndCompletes(IndexMigrationState state)
    {
        var path = CreateCurrentVersionIndex("resilience_resume_" + state);
        DowngradeVersionByte(path, "*.fln", 0);

        var originalCommit = IndexFileInspector.FindCommitFiles(path)[0];
        var staging = Path.Combine(_fixture.Path, "resume_" + state + "_staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "sentinel"), "stale");
        IndexMigrationRecovery.WriteMarker(
            path,
            CreateMarker(state, path, staging, originalCommit.Generation),
            durable: false);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal(IndexMigrationState.Published, IndexMigrationRecovery.GetState(path).State);
        Assert.False(Directory.Exists(staging));
        AssertIndexReadable(path);
        Assert.Equal(CodecConstants.FieldLengthVersion, ReadVersionByte(path, "*.fln"));
    }

    [Fact(DisplayName = "Migrate: Resume without a commit generation deletes staging and proceeds")]
    public void Migrate_Resume_MarkerWithoutCommitGeneration_DeletesStagingAndProceeds()
    {
        var path = CreateCurrentVersionIndex("resilience_resume_no_gen");
        DowngradeVersionByte(path, "*.fln", 0);

        var staging = Path.Combine(_fixture.Path, "resume_no_gen_staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "sentinel"), "stale");
        IndexMigrationRecovery.WriteMarker(
            path,
            CreateMarker(IndexMigrationState.InProgress, path, staging, null),
            durable: false);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal(IndexMigrationState.Published, IndexMigrationRecovery.GetState(path).State);
        Assert.False(Directory.Exists(staging));
        AssertIndexReadable(path);
    }

    // ═══════════════════════════════════════════════════
    //  Staging directory hygiene
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Staging directory excludes write.lock and the recovery marker")]
    public void Migrate_Staging_ExcludesWriteLockAndMarker()
    {
        var path = CreateCurrentVersionIndex("resilience_staging_excl");
        DowngradeVersionByte(path, "*.fln", 0);
        File.WriteAllText(Path.Combine(path, "write.lock"), "");

        var segmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds.Single();
        var catalog = BuildThrowingCoordinatedCatalog();
        WriteLegacyEnvelope(Path.Combine(path, segmentId + ".ca"), 1, [10]);
        WriteLegacyEnvelope(Path.Combine(path, segmentId + ".cb"), 1, [20]);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                Catalog = catalog,
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.False(result.Succeeded);
        Assert.NotNull(result.StagingDirectory);
        Assert.True(Directory.Exists(result.StagingDirectory));
        Assert.DoesNotContain(Directory.GetFiles(result.StagingDirectory),
            file => Path.GetFileName(file) == "write.lock");
        Assert.DoesNotContain(Directory.GetFiles(result.StagingDirectory),
            file => Path.GetFileName(file) == IndexMigrationRecovery.MarkerFileName);
    }

    // ═══════════════════════════════════════════════════
    //  Validation-before with a legacy-envelope term dictionary
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Validation-before proceeds with a legacy-envelope term dictionary")]
    public void Migrate_ValidateBefore_LegacyEnvelopeTermDictionary()
    {
        var path = CreateIndexWithMultipleDocuments("resilience_val_legacy_dic");
        DowngradeVersionByte(path, "*.dic", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = true,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            string.Join("; ", result.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
        Assert.Equal(CodecConstants.TermDictionaryVersion, ReadVersionByte(path, "*.dic"));
        AssertIndexReadable(path, "document");
    }

    // ═══════════════════════════════════════════════════
    //  Recovery marker edge states
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Recovery: GetState with no marker returns the None defaults")]
    public void Recovery_GetState_NoMarker_ReturnsNoneDefaults()
    {
        var path = Path.Combine(_fixture.Path, "recovery_none");
        Directory.CreateDirectory(path);

        var marker = IndexMigrationRecovery.GetState(path);

        Assert.Equal(IndexMigrationState.None, marker.State);
        Assert.Equal(path, marker.SourceDirectory);
        Assert.Equal("", marker.StagingDirectory);
        Assert.Null(marker.SourceCommitGeneration);
        Assert.Equal(DateTimeOffset.MinValue, marker.CreatedAtUtc);
        Assert.Equal(DateTimeOffset.MinValue, marker.UpdatedAtUtc);
        Assert.Empty(marker.PlannedActions);
    }

    [Fact(DisplayName = "Recovery: RollBack with no marker is a no-op")]
    public void Recovery_RollBack_NoneState_NoOp()
    {
        var path = Path.Combine(_fixture.Path, "recovery_rollback_none");
        Directory.CreateDirectory(path);

        IndexMigrationRecovery.RollBack(path);

        Assert.False(File.Exists(Path.Combine(path, IndexMigrationRecovery.MarkerFileName)));
    }

    [Fact(DisplayName = "Recovery: RollBack on a Published marker throws")]
    public void Recovery_RollBack_PublishedMarker_Throws()
    {
        var path = Path.Combine(_fixture.Path, "recovery_rollback_published");
        Directory.CreateDirectory(path);
        IndexMigrationRecovery.WriteMarker(
            path,
            CreateMarker(IndexMigrationState.Published, path, "", 1),
            durable: false);

        Assert.Throws<InvalidOperationException>(() => IndexMigrationRecovery.RollBack(path));

        Assert.True(File.Exists(Path.Combine(path, IndexMigrationRecovery.MarkerFileName)));
    }

    // ═══════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════

    private string CreateCurrentVersionIndex(string name, bool includeInt64DocValues = false)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world test migration"));
        doc.Add(new NumericField("count", 42));
        doc.Add(new StringField("id", "doc-1"));
        if (includeInt64DocValues)
        {
            doc.Add(new Int64Field("count64", 42));
            doc.Add(new Int64Field("multi64", 2));
            doc.Add(new Int64Field("multi64", 1));
        }
        writer.AddDocument(doc);
        writer.Commit();
        return path;
    }

    private string CreateIndexWithMultipleDocuments(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        for (int i = 0; i < 10; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document number {i} with some repeated words document"));
            doc.Add(new NumericField("count", i * 10));
            doc.Add(new StringField("id", $"doc-{i}"));
            writer.AddDocument(doc);
        }

        writer.Commit();
        return path;
    }

    private static void DowngradeVersionByte(string indexPath, string pattern, byte version)
    {
        foreach (var filePath in Directory.GetFiles(indexPath, pattern))
        {
            if (CodecCatalog.Default.TryMatchFile(Path.GetFileName(filePath), out var descriptor) &&
                descriptor?.FormatId == "leancorpus.postings.data" &&
                version == 0 &&
                PatchCanonicalFormatVersion(filePath, checked(descriptor.CurrentFormatVersion!.Value - 1)))
            {
                continue;
            }

            if (CodecCatalog.Default.TryMatchFile(Path.GetFileName(filePath), out descriptor) &&
                descriptor is not null &&
                TryRewriteCanonicalAsLegacyEnvelope(filePath, descriptor, version == 0
                    ? checked((byte)(descriptor.CurrentFormatVersion ?? 1))
                    : version))
            {
                continue;
            }

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.WriteByte(version);
        }
    }

    private static bool PatchCanonicalFormatVersion(string filePath, int formatVersion)
    {
        using (var input = new IndexInput(filePath))
        {
            if (input.Length < CodecFileWriter.FixedHeaderLength || unchecked((uint)input.ReadInt32()) != CodecFileWriter.Magic)
                return false;
        }

        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = sizeof(uint) + sizeof(byte) + sizeof(byte);
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, formatVersion);
        stream.Write(bytes);
        return true;
    }

    private static bool TryRewriteCanonicalAsLegacyEnvelope(
        string filePath,
        CodecFileDescriptor descriptor,
        byte legacyVersion)
    {
        byte[] body;
        long canonicalBodyStart;
        List<(string Term, long Offset)>? postingsTerms = null;
        string? postingsDictionaryPath = null;
        using (var input = new IndexInput(filePath))
        {
            if (input.Length < sizeof(int) || unchecked((uint)input.ReadInt32()) != CodecFileWriter.Magic)
                return false;

            input.Seek(0);
            using var frame = CodecFileReader.Open(input, descriptor);
            body = frame.ReadBody();
            canonicalBodyStart = frame.Metadata.BodyStart;
        }

        if (descriptor.FormatId == "leancorpus.postings.data")
        {
            postingsDictionaryPath = Path.ChangeExtension(filePath, ".dic");
            using var dictionary = Rowles.LeanCorpus.Codecs.TermDictionary.TermDictionaryReader.Open(postingsDictionaryPath);
            postingsTerms = dictionary.EnumerateAllTerms();
        }

        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte(legacyVersion);
        WriteLegacyEnvelopeLength(stream, body.Length);
        stream.Write(body);

        if (postingsTerms is not null && postingsDictionaryPath is not null)
        {
            long offsetDelta = LegacyEnvelopeHeaderSize(body.Length) - canonicalBodyStart;
            var offsets = postingsTerms.ToDictionary(
                static item => item.Term,
                item => checked(item.Offset + offsetDelta),
                StringComparer.Ordinal);
            Rowles.LeanCorpus.Codecs.TermDictionary.TermDictionaryWriter.Write(
                postingsDictionaryPath,
                offsets.Keys.OrderBy(static term => term, StringComparer.Ordinal).ToList(),
                offsets,
                durable: true);
        }
        return true;
    }

    private static void WriteLegacyEnvelopeLength(Stream stream, long value)
    {
        ulong encoded = checked((ulong)value << 1);
        while (encoded >= 0x80)
        {
            stream.WriteByte((byte)(encoded | 0x80));
            encoded >>= 7;
        }
        stream.WriteByte((byte)encoded);
    }

    private static byte ReadVersionByte(string indexPath, string pattern)
    {
        var path = Directory.GetFiles(indexPath, pattern).Single();
        using (var input = new IndexInput(path))
        {
            if (input.Length >= sizeof(int) && unchecked((uint)input.ReadInt32()) == CodecFileWriter.Magic)
            {
                input.Seek(0);
                using var frame = CodecFileReader.Open(input, CodecCatalog.Default);
                return checked((byte)frame.Metadata.FormatVersion);
            }
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (byte)stream.ReadByte();
    }

    private static int LegacyEnvelopeHeaderSize(int bodyLength)
    {
        ulong encoded = checked((ulong)bodyLength << 1);
        int lengthBytes = 1;
        while (encoded >= 0x80)
        {
            lengthBytes++;
            encoded >>= 7;
        }
        return sizeof(byte) + lengthBytes;
    }

    private static void WriteLegacyEnvelope(string path, byte version, byte[] body)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte(version);
        WriteLegacyEnvelopeLength(stream, body.Length);
        stream.Write(body);
    }

    private static void AssertIndexReadable(string indexPath, string term = "hello")
    {
        using var directory = new MMapDirectory(indexPath);
        var compatibility = IndexCompatibility.Check(directory);
        Assert.True(
            compatibility.Status == IndexCompatibilityStatus.Compatible,
            $"Compatibility was {compatibility.Status}: {string.Join("; ", compatibility.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new TermQuery("body", term), 10);
        Assert.True(results.TotalHits > 0);
    }

    private static byte ReadSingleBodyByte(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        using var body = frame.OpenBodyInput();
        Assert.Equal(1, body.Length);
        return body.ReadByte();
    }

    private static CodecFileDescriptor CreateCoordinatedDescriptor(
        string formatId,
        string familyId,
        string extension)
        => new(
            formatId,
            familyId,
            formatId,
            CodecFileMatcher.Extension(extension),
            currentFormatVersion: 2,
            supportedVersions:
            [
                new CodecVersionDescriptor(
                    1,
                    "legacy",
                    legacyFraming: CodecLegacyFraming.CodecKitEnvelope,
                    migrationBehaviour: CodecMigrationBehaviour.CoordinatedRewrite),
                new CodecVersionDescriptor(
                    2,
                    "current",
                    isWritable: true,
                    migrationBehaviour: CodecMigrationBehaviour.CoordinatedRewrite),
            ],
            accessKind: CodecAccessKind.Streaming,
            currentFraming: CodecFramingPolicy.Canonical,
            checksumPolicy: CodecChecksumPolicy.XxHash64,
            migrationBehaviour: CodecMigrationBehaviour.CoordinatedRewrite,
            temporaryFileMatchers: [CodecFileMatcher.ExtensionWithTrailingSuffix(extension, ".codec.tmp")]);

    private static CodecFileDescriptor CreateHandlerDescriptor(
        string formatId, string familyId, string extension,
        ICodecFileMigrationHandler handler)
        => new(
            formatId, familyId, formatId,
            CodecFileMatcher.Extension(extension),
            currentFormatVersion: 2,
            supportedVersions:
            [
                new CodecVersionDescriptor(1, "legacy",
                    legacyFraming: CodecLegacyFraming.CodecKitEnvelope,
                    migrationBehaviour: CodecMigrationBehaviour.Rewrite),
                new CodecVersionDescriptor(2, "current",
                    isWritable: true,
                    migrationBehaviour: CodecMigrationBehaviour.Rewrite),
            ],
            accessKind: CodecAccessKind.Streaming,
            currentFraming: CodecFramingPolicy.Canonical,
            checksumPolicy: CodecChecksumPolicy.XxHash64,
            migrationBehaviour: CodecMigrationBehaviour.Rewrite,
            temporaryFileMatchers: [CodecFileMatcher.ExtensionWithTrailingSuffix(extension, ".codec.tmp")],
            migrationHandler: handler);

    private static CodecCatalog BuildThrowingCoordinatedCatalog()
    {
        var caDescriptor = CreateCoordinatedDescriptor("unit-test.coord.throw.first", "unit-test.coord.throw", ".ca");
        var cbDescriptor = CreateCoordinatedDescriptor("unit-test.coord.throw.second", "unit-test.coord.throw", ".cb");
        return new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor(
                "unit-test.coord.throw",
                "Throwing coordinated migration",
                [caDescriptor, cbDescriptor],
                migrationCoordinator: new ThrowingFamilyMigrationCoordinator()))
            .Build();
    }

    private static IndexMigrationMarker CreateMarker(
        IndexMigrationState state, string sourcePath, string stagingPath, int? commitGeneration)
    {
        var now = DateTimeOffset.UtcNow;
        return new IndexMigrationMarker
        {
            State = state,
            SourceDirectory = sourcePath,
            StagingDirectory = stagingPath,
            SourceCommitGeneration = commitGeneration,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PlannedActions = []
        };
    }

    private static string[] SnapshotSourceFiles(string path)
        => Directory.GetFiles(path)
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

    private static string[] SnapshotSourceFilesWithoutMarker(string path)
        => Directory.GetFiles(path)
            .Where(static file => Path.GetFileName(file) != IndexMigrationRecovery.MarkerFileName)
            .OrderBy(static file => file, StringComparer.Ordinal)
            .ToArray();

    private sealed class ThrowingMigrationHandler : ICodecFileMigrationHandler
    {
        public void Migrate(IndexInput sourceBody, IndexOutput targetBody)
            => throw new InvalidDataException("injected migration failure");
    }

    private sealed class ThrowingFamilyMigrationCoordinator : ICodecFamilyMigrationCoordinator
    {
        public void Migrate(
            IReadOnlyDictionary<string, IndexInput> sourceBodies,
            IReadOnlyDictionary<string, IndexOutput> targetBodies)
            => throw new InvalidDataException("injected coordination failure");
    }

    private sealed class IncrementHandler : ICodecFileMigrationHandler
    {
        public void Migrate(IndexInput sourceBody, IndexOutput targetBody)
            => targetBody.WriteByte(checked((byte)(sourceBody.ReadByte() + 1)));
    }
}
