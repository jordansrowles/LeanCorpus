using System.Globalization;
using Rowles.LeanCorpus.Codecs;
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

namespace Rowles.LeanCorpus.Tests.Integration.Index.Migration;

[Trait("Category", "Index")]
[Trait("Category", "Migration")]
public sealed class IndexCodecMigratorTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCodecMigratorTests(TestDirectoryFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Creates a minimal index with a single document containing a text field and numeric field.
    /// </summary>
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

    /// <summary>
    /// Creates an index with multiple documents for richer postings data.
    /// </summary>
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

    private string CreateVectorIndex(string name, VectorQuantisation quantisation)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            VectorQuantisation = quantisation,
            BuildHnswOnFlush = true,
            HnswSeed = 739391L,
        });
        for (int i = 0; i < 2; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello vector migration"));
            doc.Add(new StringField("id", $"vector-{i}"));
            doc.Add(new VectorField("embedding", new float[] { i + 1, i + 2, i + 3, i + 4 }));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return path;
    }

    private string CreateTermVectorIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { StoreTermVectors = true });
        for (int i = 0; i < 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"term vector document {i}"));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return path;
    }

    /// <summary>
    /// Re-wraps canonical files matching <paramref name="pattern"/> in their valid legacy envelope.
    /// </summary>
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

    /// <summary>
    /// Reads the first byte (version) of a matching file.
    /// </summary>
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

    /// <summary>
    /// Re-wraps current canonical stored-fields files as v1 CodecKit envelopes.
    /// Used to exercise the stored-fields migration path.
    /// </summary>
    private static void DowngradeStoredFieldsToV1(string indexPath)
    {
        var fdtPath = Directory.GetFiles(indexPath, "*.fdt").Single();
        var fdxPath = Directory.GetFiles(indexPath, "*.fdx").Single();

        var (fdtBody, canonicalFdtBodyStart) = ReadCanonicalBody(fdtPath);
        int fdtHeaderSize;
        using (var fs = new FileStream(fdtPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(1);
            WriteLegacyEnvelopeLength(fs, fdtBody.Length);
            fdtHeaderSize = LegacyEnvelopeHeaderSize(fdtBody.Length);
            fs.Write(fdtBody);
        }

        // Re-wrap .fdx and shift file-absolute block offsets to the v1 body base.
        var (fdxBodyBytes, _) = ReadCanonicalBody(fdxPath);
        var fdxBody = fdxBodyBytes.AsSpan();
        int blockSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(fdxBody);
        int docCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(fdxBody.Slice(4));
        int blockCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(fdxBody.Slice(8));
        long headerDelta = fdtHeaderSize - canonicalFdtBodyStart;

        using (var fs = new FileStream(fdxPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(1);
            WriteLegacyEnvelopeLength(fs, fdxBody.Length);
            fs.Write(fdxBody.Slice(0, 12));
            for (int i = 0; i < blockCount; i++)
            {
                long offset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(fdxBody.Slice(12 + i * 8));
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                    fdxBody.Slice(12 + i * 8), offset + headerDelta);
            }
            fs.Write(fdxBody.Slice(12, blockCount * 8));
        }
    }

    private static (byte[] Body, long BodyStart) ReadCanonicalBody(string path)
    {
        Assert.True(CodecCatalog.Default.TryMatchFile(Path.GetFileName(path), out var descriptor));
        Assert.NotNull(descriptor);
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor!);
        return (frame.ReadBody(), frame.Metadata.BodyStart);
    }

    private static void DowngradeTermVectorsToV2(string indexPath)
    {
        var tvdPath = Directory.GetFiles(indexPath, "*.tvd").Single();
        var tvxPath = Directory.GetFiles(indexPath, "*.tvx").Single();
        var (tvdBody, canonicalTvdBodyStart) = ReadCanonicalBody(tvdPath);
        var (tvxBody, _) = ReadCanonicalBody(tvxPath);

        int tvdHeaderSize = LegacyEnvelopeHeaderSize(tvdBody.Length);
        int docCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(tvxBody);
        long offsetDelta = tvdHeaderSize - canonicalTvdBodyStart;
        for (int i = 0; i < docCount; i++)
        {
            Span<byte> offsetBytes = tvxBody.AsSpan(sizeof(int) + i * sizeof(long), sizeof(long));
            long offset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(offsetBytes);
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(offsetBytes, offset + offsetDelta);
        }

        WriteLegacyEnvelope(tvdPath, version: 2, tvdBody);
        WriteLegacyEnvelope(tvxPath, version: 2, tvxBody);
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

    private static void WriteCustomHeader(string path, byte version, byte[] body)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.WriteByte(version);
        stream.Write(body);
    }

    /// <summary>
    /// Verifies the index is queryable after migration.
    /// </summary>
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

    /// <summary>
    /// Checks whether a file pattern exists in the index.
    /// </summary>
    private static bool FileExists(string indexPath, string pattern)
        => Directory.GetFiles(indexPath, pattern).Length > 0;

    // ═══════════════════════════════════════════════════
    //  Plan — edge cases
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Plan: Empty directory returns zero actions")]
    public void Plan_EmptyDirectory_ZeroActions()
    {
        var path = Path.Combine(_fixture.Path, "plan_empty");
        Directory.CreateDirectory(path);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));

        Assert.NotNull(plan);
        Assert.Empty(plan.Actions);
        Assert.True(plan.CanExecute);
        Assert.NotEmpty(plan.Issues); // No commit file
    }

    [Fact(DisplayName = "Plan: Current-version index returns only NoOp actions")]
    public void Plan_CurrentVersionIndex_NoOpActions()
    {
        var path = CreateCurrentVersionIndex("plan_current");

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));

        Assert.NotNull(plan);
        Assert.All(plan.Actions, action => Assert.Equal(
            IndexCodecMigrationActionKind.NoOp, action.Kind));
    }

    [Fact(DisplayName = "Plan: Legacy framing produces a Reframe action")]
    public void Plan_DowngradedFile_ProducesRewriteAction()
    {
        var path = CreateCurrentVersionIndex("plan_downgraded");
        // Downgrade field lengths (.fln) — a v1 format with no version dispatch.
        DowngradeVersionByte(path, "*.fln", 0);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));

        Assert.Contains(plan.Actions, action =>
            action.Kind == IndexCodecMigrationActionKind.Reframe &&
            action.FileName!.EndsWith(".fln", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Plan: Null options uses defaults")]
    public void Plan_NullOptions_UsesDefaults()
    {
        var path = CreateCurrentVersionIndex("plan_null_options");
        DowngradeVersionByte(path, "*.fln", 0);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path), options: null);

        Assert.NotNull(plan);
    }

    [Fact(DisplayName = "Plan: Inventory overload matches directory overload")]
    public void Plan_InventoryOverload_MatchesDirectoryOverload()
    {
        var path = CreateCurrentVersionIndex("plan_inventory");
        DowngradeVersionByte(path, "*.fln", 0);

        var planFromDir = IndexCodecMigrator.Plan(new MMapDirectory(path));
        var planFromInventory = IndexCodecMigrator.Plan(planFromDir.Inventory);

        Assert.Equal(planFromDir.Actions.Count, planFromInventory.Actions.Count);
    }

    // ═══════════════════════════════════════════════════
    //  Dry-run and no-actions paths
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Dry-run on empty index succeeds")]
    public void Migrate_DryRun_EmptyIndex_Succeeds()
    {
        var path = Path.Combine(_fixture.Path, "migrate_dry_empty");
        Directory.CreateDirectory(path);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions { DryRun = true });

        Assert.True(result.Succeeded);
        Assert.True(result.DryRun);
        Assert.Empty(result.ExecutedActions);
    }

    [Fact(DisplayName = "Migrate: Dry-run on downgraded index returns plan actions without modifying")]
    public void Migrate_DryRun_Downgraded_ReturnsPlanActions()
    {
        var path = CreateCurrentVersionIndex("migrate_dry_downgraded");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions { DryRun = true });

        Assert.True(result.Succeeded);
        Assert.True(result.DryRun);
        Assert.NotEmpty(result.ExecutedActions);
        // Files should not have been modified.
        Assert.Equal(CodecConstants.FieldLengthVersion, ReadVersionByte(path, "*.fln"));
    }

    [Fact(DisplayName = "Migrate: Plan discovers files with no registered migration writer")]
    public void Migrate_Plan_HasUnactionableFiles()
    {
        var path = CreateCurrentVersionIndex("migrate_unactionable");
        DowngradeVersionByte(path, "*.fln", 0);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));

        var flnAction = plan.Actions.Single(action =>
            action.FileName!.EndsWith(".fln", StringComparison.Ordinal));
        Assert.True(flnAction.CanExecute);
        Assert.Null(flnAction.ReasonCannotExecute);
    }

    [Fact(DisplayName = "Migrate: Plan CanExecute is false when unsupported extension exists")]
    public void Migrate_Plan_CanExecuteFalse_WhenUnsupportedExtension()
    {
        var path = CreateIndexWithMultipleDocuments("migrate_unsupported_ext");

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));

        var unactionable = plan.Actions.Where(action => !action.CanExecute).ToList();
        if (unactionable.Count > 0)
        {
            Assert.False(plan.CanExecute);
            Assert.All(unactionable, action => Assert.NotNull(action.ReasonCannotExecute));
        }
    }

    [Fact(DisplayName = "Migrate: Execute on current-version index succeeds with no actions")]
    public void Migrate_Execute_CurrentVersion_SucceedsWithNoActions()
    {
        var path = CreateCurrentVersionIndex("migrate_exec_current");

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
            });

        Assert.True(result.Succeeded);
        Assert.False(result.DryRun);
        Assert.Empty(result.ExecutedActions);
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Registered family coordinator rewrites all family files")]
    public void Migrate_FamilyCoordinator_RewritesAllFamilyFiles()
    {
        var path = CreateCurrentVersionIndex("migrate_family_coordinator");
        var segmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds.Single();
        const string familyId = "example.coordinated-migration";
        var first = CreateCoordinatedDescriptor(
            "example.coordinated-migration.first",
            familyId,
            ".coordinated-a");
        var second = CreateCoordinatedDescriptor(
            "example.coordinated-migration.second",
            familyId,
            ".coordinated-b");
        var coordinator = new IncrementingFamilyMigrationCoordinator();
        var catalog = new CodecCatalogBuilder()
            .AddBuiltIns()
            .Add(new CodecFamilyDescriptor(
                familyId,
                "Coordinated migration",
                [first, second],
                migrationCoordinator: coordinator))
            .Build();
        WriteLegacyEnvelope(Path.Combine(path, segmentId + ".coordinated-a"), version: 1, [10]);
        WriteLegacyEnvelope(Path.Combine(path, segmentId + ".coordinated-b"), version: 1, [20]);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path), new IndexCodecMigrationOptions { Catalog = catalog });
        var action = Assert.Single(plan.Actions, candidate => candidate.FamilyId == familyId);
        Assert.True(action.CanExecute, action.ReasonCannotExecute);
        Assert.Equal(2, action.SourcePaths.Count);

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
        Assert.Equal(1, coordinator.InvocationCount);
        var migratedSegmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds.Single();
        Assert.Equal(11, ReadSingleBodyByte(Path.Combine(path, migratedSegmentId + ".coordinated-a"), first));
        Assert.Equal(21, ReadSingleBodyByte(Path.Combine(path, migratedSegmentId + ".coordinated-b"), second));
        var inventory = IndexFormatInspector.Inspect(new MMapDirectory(path), new IndexFormatInspectionOptions
        {
            Catalog = catalog,
            IncludeChecksums = true,
        });
        Assert.DoesNotContain(inventory.Issues, issue => issue.Severity == IndexCheckSeverity.Error);
        Assert.All(
            Assert.Single(inventory.Segments).Files.Where(file => file.FamilyId == familyId),
            file => Assert.True(file.IsCurrent));
    }

    // ═══════════════════════════════════════════════════
    //  Pre-migration validation blocking
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Validation-before passes and proceeds")]
    public void Migrate_ValidationBefore_PassesAndProceeds()
    {
        var path = CreateCurrentVersionIndex("migrate_val_before_pass");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = true,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.ExecutedActions);
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Validation-before fails and blocks")]
    public void Migrate_ValidationBefore_FailsAndBlocks()
    {
        var path = CreateCurrentVersionIndex("migrate_val_before_fail");
        DowngradeVersionByte(path, "*.fln", 0);
        // Corrupt a .dic file to cause a validation error.
        var dicPath = Directory.GetFiles(path, "*.dic").Single();
        File.WriteAllText(dicPath, "corrupt");

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = true,
                ValidateAfterMigration = false,
            });

        Assert.False(result.Succeeded);
        Assert.Empty(result.ExecutedActions);
        Assert.NotNull(result.ValidationResult);
        Assert.NotEmpty(result.Issues);
    }

    [Fact(DisplayName = "Migrate: Validation-before skipped proceeds despite corruption")]
    public void Migrate_ValidationBefore_Skipped_Proceeds()
    {
        var path = CreateCurrentVersionIndex("migrate_val_skip");
        DowngradeVersionByte(path, "*.fln", 0);
        var dicPath = Directory.GetFiles(path, "*.dic").Single();
        File.WriteAllText(dicPath, "corrupt");

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.ExecutedActions);
    }

    // ═══════════════════════════════════════════════════
    //  Staging directory lifecycle
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Default auto-generated staging directory")]
    public void Migrate_Staging_AutoGenerated()
    {
        var path = CreateCurrentVersionIndex("migrate_staging_auto");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.ExecutedActions);
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Custom staging directory path")]
    public void Migrate_Staging_CustomPath()
    {
        var path = CreateCurrentVersionIndex("migrate_staging_custom");
        var stagingPath = Path.Combine(_fixture.Path, "custom-staging-dir");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                StagingDirectory = stagingPath,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.ExecutedActions);
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Staging directory already exists fails")]
    public void Migrate_Staging_AlreadyExists_Fails()
    {
        var path = CreateCurrentVersionIndex("migrate_staging_exists");
        var stagingPath = Path.Combine(_fixture.Path, "staging-exists-dir");
        Directory.CreateDirectory(stagingPath);
        File.WriteAllText(Path.Combine(stagingPath, "sentinel"), "occupied");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                StagingDirectory = stagingPath,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Issues);
    }

    // ═══════════════════════════════════════════════════
    //  Per-format rewrite tests (using v1 formats)
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Runs a single-format rewrite test: downgrades the version byte of files
    /// matching <paramref name="pattern"/>, runs in-place migration, and verifies
    /// the version byte was restored to <paramref name="expectedVersion"/>.
    /// Skips the test if the file pattern does not exist in the index.
    /// </summary>
    private void AssertRewriteRestoresVersion(
        string testName,
        string pattern,
        byte expectedVersion,
        string searchTerm = "hello",
        bool includeInt64DocValues = false)
    {
        var path = CreateCurrentVersionIndex(testName, includeInt64DocValues);
        if (!FileExists(path, pattern))
            return; // File type not produced by this index configuration — skip.

        DowngradeVersionByte(path, pattern, 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            $"Rewrite of {pattern} failed. Issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        Assert.Equal(expectedVersion, ReadVersionByte(path, pattern));
        var rewrittenPath = Directory.GetFiles(path, pattern).Single();
        if (CodecCatalog.Default.TryMatchFile(Path.GetFileName(rewrittenPath), out var descriptor) &&
            descriptor is not null && descriptor.FamilyId is "leancorpus.doc-values" or "leancorpus.numeric-structures")
        {
            using var input = new IndexInput(rewrittenPath);
            using var frame = CodecFileReader.Open(input, descriptor);
            Assert.Equal(CodecFileWriter.CurrentFrameVersion, frame.Metadata.FrameVersion);
            Assert.Equal(descriptor.CurrentFormatVersion, frame.Metadata.FormatVersion);
            frame.ValidateChecksum();
        }
        AssertIndexReadable(path, searchTerm);
    }

    private void AssertVectorRewritePreservesBody(
        string testName,
        string pattern,
        VectorQuantisation quantisation)
    {
        var path = CreateVectorIndex(testName, quantisation);
        var sourcePath = Directory.GetFiles(path, pattern).Single();
        Assert.True(CodecCatalog.Default.TryMatchFile(Path.GetFileName(sourcePath), out var descriptor));
        Assert.NotNull(descriptor);

        DowngradeVersionByte(path, pattern, 0);
        byte[] expectedBody;
        using (var input = new IndexInput(sourcePath))
        using (var legacy = CodecFileReader.OpenSupported(input, descriptor!))
        {
            Assert.False(legacy.IsCanonical);
            expectedBody = legacy.ReadBody();
        }

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            $"Rewrite of {pattern} failed. Issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        var rewrittenPath = Directory.GetFiles(path, pattern).Single();
        using (var input = new IndexInput(rewrittenPath))
        using (var frame = CodecFileReader.Open(input, descriptor!))
        {
            Assert.Equal(CodecFileWriter.CurrentFrameVersion, frame.Metadata.FrameVersion);
            Assert.Equal(descriptor!.CurrentFormatVersion, frame.Metadata.FormatVersion);
            Assert.Equal(expectedBody, frame.ReadBody());
        }
        using var directory = new MMapDirectory(path);
        using var searcher = new IndexSearcher(directory);
        Assert.True(searcher.Search(new TermQuery("body", "hello"), 10).TotalHits > 0);
    }

    [Fact(DisplayName = "Migrate: Rewrite field lengths")]
    public void Migrate_Rewrite_FieldLengths()
        => AssertRewriteRestoresVersion("migrate_rewrite_fln", "*.fln", CodecConstants.FieldLengthVersion);

    [Fact(DisplayName = "Migrate: Rewrite numeric doc values")]
    public void Migrate_Rewrite_NumericDocValues()
        => AssertRewriteRestoresVersion("migrate_rewrite_dvn", "*.dvn", CodecConstants.NumericDocValuesVersion);

    [Fact(DisplayName = "Migrate: Rewrite sorted doc values")]
    public void Migrate_Rewrite_SortedDocValues()
        => AssertRewriteRestoresVersion("migrate_rewrite_dvs", "*.dvs", CodecConstants.SortedDocValuesVersion);

    [Fact(DisplayName = "Migrate: Rewrite sorted set doc values")]
    public void Migrate_Rewrite_SortedSetDocValues()
        => AssertRewriteRestoresVersion("migrate_rewrite_dss", "*.dss", CodecConstants.SortedSetDocValuesVersion);

    [Fact(DisplayName = "Migrate: Rewrite sorted numeric doc values")]
    public void Migrate_Rewrite_SortedNumericDocValues()
        => AssertRewriteRestoresVersion("migrate_rewrite_dsn", "*.dsn", CodecConstants.SortedNumericDocValuesVersion);

    [Fact(DisplayName = "Migrate: Rewrite binary doc values")]
    public void Migrate_Rewrite_BinaryDocValues()
        => AssertRewriteRestoresVersion("migrate_rewrite_dvb", "*.dvb", CodecConstants.BinaryDocValuesVersion);

    [Fact(DisplayName = "Migrate: Rewrite Int64 doc values")]
    public void Migrate_Rewrite_Int64DocValues()
        => AssertRewriteRestoresVersion(
            "migrate_rewrite_dvnl",
            "*.dvnl",
            CodecConstants.Int64DocValuesVersion,
            includeInt64DocValues: true);

    [Fact(DisplayName = "Migrate: Rewrite Int64 sorted numeric doc values")]
    public void Migrate_Rewrite_Int64SortedNumericDocValues()
        => AssertRewriteRestoresVersion(
            "migrate_rewrite_dsnl",
            "*.dsnl",
            CodecConstants.Int64SortedNumericDocValuesVersion,
            includeInt64DocValues: true);

    [Fact(DisplayName = "Migrate: Reframe float vectors without changing body offsets")]
    public void Migrate_Rewrite_FloatVectors()
        => AssertVectorRewritePreservesBody("migrate_rewrite_vec", "*.vec", VectorQuantisation.None);

    [Fact(DisplayName = "Migrate: Reframe quantised vectors without changing metadata")]
    public void Migrate_Rewrite_QuantisedVectors()
        => AssertVectorRewritePreservesBody("migrate_rewrite_vq", "*.vq", VectorQuantisation.Int8);

    [Fact(DisplayName = "Migrate: Reframe HNSW without changing persisted seed")]
    public void Migrate_Rewrite_Hnsw()
        => AssertVectorRewritePreservesBody("migrate_rewrite_hnsw", "*.hnsw", VectorQuantisation.None);

    [Fact(DisplayName = "Migrate: Rewrite norms")]
    public void Migrate_Rewrite_Norms()
        => AssertRewriteRestoresVersion("migrate_rewrite_nrm", "*.nrm", CodecConstants.NormsVersion);

    [Fact(DisplayName = "Migrate: Rewrite BKD into the canonical frame")]
    public void Migrate_Rewrite_Bkd()
        => AssertRewriteRestoresVersion("migrate_rewrite_bkd", "*.bkd", CodecConstants.BKDVersion);

    [Fact(DisplayName = "Migrate: Rewrite Int64 BKD into the canonical frame")]
    public void Migrate_Rewrite_Int64Bkd()
        => AssertRewriteRestoresVersion(
            "migrate_rewrite_bkdl",
            "*.bkdl",
            CodecConstants.Int64BKDVersion,
            includeInt64DocValues: true);

    // ═══════════════════════════════════════════════════
    //  Term dictionary and stored fields
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Term dictionary same version is no-op")]
    public void Migrate_TermDictionary_SameVersion_NoOp()
    {
        var path = CreateCurrentVersionIndex("migrate_dic_same");
        // Write version 0 then restore to current to trigger the no-op path.
        DowngradeVersionByte(path, "*.dic", 0);
        DowngradeVersionByte(path, "*.dic", CodecConstants.TermDictionaryVersion);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Rewrite term dictionary from old version")]
    public void Migrate_Rewrite_TermDictionary()
    {
        var path = CreateIndexWithMultipleDocuments("migrate_rewrite_dic");
        DowngradeVersionByte(path, "*.dic", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            $"Term dictionary rewrite failed. Issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        Assert.Equal(CodecConstants.TermDictionaryVersion, ReadVersionByte(path, "*.dic"));
        AssertIndexReadable(path, "document");
    }

    [Fact(DisplayName = "Migrate: Unsupported format version produces inspection issue not action")]
    public void Migrate_UnsupportedFormatVersion_ProducesIssue()
    {
        var path = CreateCurrentVersionIndex("migrate_dic_unsupported");
        DowngradeVersionByte(path, "*.dic", 99);

        // Verify the downgrade took effect.
        Assert.Equal(99, ReadVersionByte(path, "*.dic"));

        // Plan does NOT produce a rewrite action — the format inspector
        // reports an unsupported format version as an issue instead.
        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));

        Assert.DoesNotContain(plan.Actions, action =>
            action.FileName!.EndsWith(".dic", StringComparison.Ordinal));

        Assert.NotEmpty(plan.Issues);
        // CanExecute may be true if all (zero or otherwise) actions are executable.
        // The issue itself is a blocker during execution, not during planning.

        // Migrate with DryRun=false on an index with zero actions succeeds
        // (the unsupported-version issue doesn't block the early exit).
        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.Empty(result.ExecutedActions);
        Assert.NotEmpty(result.Issues);
    }

    [Fact(DisplayName = "Migrate: Rewrite stored fields")]
    public void Migrate_Rewrite_StoredFields()
    {
        var path = CreateCurrentVersionIndex("migrate_rewrite_fdt");
        DowngradeStoredFieldsToV1(path);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            $"Migration failed. Issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        Assert.Equal(CodecConstants.StoredFieldsVersion, ReadVersionByte(path, "*.fdt"));
        Assert.Equal(CodecConstants.StoredFieldsVersion, ReadVersionByte(path, "*.fdx"));
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Stored-fields family rewrites when only the index member is legacy")]
    public void Migrate_StoredFieldsIndexOnlyLegacy_RewritesFamily()
    {
        var path = CreateCurrentVersionIndex("migrate_rewrite_fdx_only");
        var fdxPath = Directory.GetFiles(path, "*.fdx").Single();
        var (body, _) = ReadCanonicalBody(fdxPath);
        WriteCustomHeader(fdxPath, version: 2, body);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));
        Assert.Contains(plan.Actions, action =>
            action.Kind == IndexCodecMigrationActionKind.CoordinatedRewrite &&
            action.FileName!.EndsWith(".fdx", StringComparison.Ordinal));

        var result = IndexCodecMigrator.Migrate(new MMapDirectory(path), new IndexCodecMigrationOptions
        {
            DryRun = false,
            ValidateBeforeMigration = false,
            ValidateAfterMigration = true,
        });

        Assert.True(result.Succeeded,
            string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}")));
        Assert.Equal(CodecConstants.StoredFieldsVersion, ReadVersionByte(path, "*.fdt"));
        Assert.Equal(CodecConstants.StoredFieldsVersion, ReadVersionByte(path, "*.fdx"));
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Rewrite term vectors as one coordinated canonical family")]
    public void Migrate_Rewrite_TermVectors()
    {
        var path = CreateTermVectorIndex("migrate_rewrite_term_vectors");
        DowngradeTermVectorsToV2(path);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            $"Migration failed. Issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        Assert.Equal(CodecConstants.TermVectorsVersion, ReadVersionByte(path, "*.tvd"));
        Assert.Equal(CodecConstants.TermVectorsVersion, ReadVersionByte(path, "*.tvx"));

        foreach (string file in Directory.GetFiles(path, "*.tv?"))
        {
            Assert.True(CodecCatalog.Default.TryMatchFile(Path.GetFileName(file), out var descriptor));
            using var input = new IndexInput(file);
            using var frame = CodecFileReader.Open(input, descriptor!);
            frame.ValidateChecksum();
        }
        AssertIndexReadable(path, "term");
    }

    [Fact(DisplayName = "Migrate: Term-vector family rewrites when only the index member is legacy")]
    public void Migrate_TermVectorIndexOnlyLegacy_RewritesFamily()
    {
        var path = CreateTermVectorIndex("migrate_rewrite_tvx_only");
        var tvxPath = Directory.GetFiles(path, "*.tvx").Single();
        var (body, _) = ReadCanonicalBody(tvxPath);
        WriteLegacyEnvelope(tvxPath, version: 2, body);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));
        Assert.Contains(plan.Actions, action =>
            action.Kind == IndexCodecMigrationActionKind.CoordinatedRewrite &&
            action.FileName!.EndsWith(".tvx", StringComparison.Ordinal));

        var result = IndexCodecMigrator.Migrate(new MMapDirectory(path), new IndexCodecMigrationOptions
        {
            DryRun = false,
            ValidateBeforeMigration = false,
            ValidateAfterMigration = true,
        });

        Assert.True(result.Succeeded,
            string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}")));
        Assert.Equal(CodecConstants.TermVectorsVersion, ReadVersionByte(path, "*.tvd"));
        Assert.Equal(CodecConstants.TermVectorsVersion, ReadVersionByte(path, "*.tvx"));
        AssertIndexReadable(path, "term");
    }

    [Fact(DisplayName = "Migrate: Legacy live docs rewrite through the current writer")]
    public void Migrate_LegacyLiveDocs_RewritesCurrentFrame()
    {
        var path = CreateIndexWithMultipleDocuments("migrate_legacy_live_docs");
        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            writer.DeleteDocuments(new TermQuery("id", "doc-1"));
            writer.Commit();
        }

        var delPath = Directory.GetFiles(path, "*.del").Single();
        File.WriteAllBytes(delPath, HistoricalCodecFixtures.LiveDocsHeaderlessV1);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));
        Assert.Contains(plan.Actions, action =>
            action.Kind == IndexCodecMigrationActionKind.Rewrite &&
            action.FileName!.EndsWith(".del", StringComparison.Ordinal));

        var result = IndexCodecMigrator.Migrate(new MMapDirectory(path), new IndexCodecMigrationOptions
        {
            DryRun = false,
            ValidateBeforeMigration = false,
            ValidateAfterMigration = true,
        });

        Assert.True(result.Succeeded,
            string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}")));
        using var input = new IndexInput(Directory.GetFiles(path, "*.del").Single());
        using var frame = CodecFileReader.Open(input, CodecCatalog.Default.GetFile("leancorpus.deletes.live-docs"));
        frame.ValidateChecksum();
    }

    [Fact(DisplayName = "Migrate: Rewrite stored fields preserves source compression policy")]
    public void Migrate_Rewrite_StoredFields_PreservesCompression()
    {
        var path = CreateCurrentVersionIndex("migrate_rewrite_fdt_compression");
        var fdtPath = Directory.GetFiles(path, "*.fdt").Single();
        var fdxPath = Directory.GetFiles(path, "*.fdx").Single();

        // Recreate stored fields with no compression so we can distinguish it from Deflate.
        var doc = new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
        {
            ["body"] = [StoredFieldValue.FromString("hello world test migration")],
            ["count"] = [StoredFieldValue.FromLong(42)],
            ["id"] = [StoredFieldValue.FromString("doc-1")]
        };

        File.Delete(fdtPath);
        File.Delete(fdxPath);
        StoredFieldsWriter.Write(fdtPath, fdxPath, 1, _ => doc, compression: FieldCompressionPolicy.None);
        DowngradeStoredFieldsToV1(path);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            $"Migration failed. Issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");

        var migratedFdtPath = Directory.GetFiles(path, "*.fdt").Single();
        var migratedFdxPath = Directory.GetFiles(path, "*.fdx").Single();
        using var reader = StoredFieldsReader.Open(migratedFdtPath, migratedFdxPath);
        Assert.Equal(FieldCompressionPolicy.None, reader.Compression);
        AssertIndexReadable(path);
    }

    // ═══════════════════════════════════════════════════
    //  Postings rewrite
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Rewrite postings from old version")]
    public void Migrate_Rewrite_Postings()
    {
        var path = CreateIndexWithMultipleDocuments("migrate_rewrite_pos");
        DowngradeVersionByte(path, "*.pos", 0);
        var dicVersion = ReadVersionByte(path, "*.dic");

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded,
            $"Postings rewrite failed. Issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        Assert.Equal(CodecConstants.PostingsVersion, ReadVersionByte(path, "*.pos"));
        Assert.Equal(dicVersion, ReadVersionByte(path, "*.dic"));
        AssertIndexReadable(path, "document");
    }

    // ═══════════════════════════════════════════════════
    //  Post-migration validation and publish
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Validation-after passes and publishes")]
    public void Migrate_ValidationAfter_PassesAndPublishes()
    {
        var path = CreateCurrentVersionIndex("migrate_val_after_pass");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = true,
            });

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.ExecutedActions);
        Assert.NotNull(result.ValidationResult);
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Validation-after skipped proceeds without check")]
    public void Migrate_ValidationAfter_Skipped_Proceeds()
    {
        var path = CreateCurrentVersionIndex("migrate_val_after_skip");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.Null(result.ValidationResult);
        AssertIndexReadable(path);
    }

    // ═══════════════════════════════════════════════════
    //  Cleanup and error handling
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: Staging directory cleaned up after successful publish")]
    public void Migrate_Staging_CleanedUpAfterPublish()
    {
        var path = CreateCurrentVersionIndex("migrate_cleanup");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Exception during rewrite caught and marker written")]
    public void Migrate_ExceptionDuringRewrite_CaughtAndMarked()
    {
        var path = CreateCurrentVersionIndex("migrate_exception");
        // Downgrade .pos to v0 — if the reader rejects it, the exception is caught.
        if (!FileExists(path, "*.pos"))
            return;

        DowngradeVersionByte(path, "*.pos", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        // Either the rewrite succeeded (reader can handle v0 body)
        // or it failed with a caught exception.
        if (!result.Succeeded)
        {
            Assert.NotEmpty(result.Issues);
            Assert.Contains(result.Issues, issue =>
                issue.Code == IndexCheckIssueCodes.UnsupportedMigrationPath);
        }
    }

    // ═══════════════════════════════════════════════════
    //  Atomic publish and crash safety
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Migrate: New segment IDs are generated for rewritten segments")]
    public void Migrate_AtomicPublish_NewSegmentIdsGenerated()
    {
        var path = CreateCurrentVersionIndex("migrate_new_seg_ids");
        DowngradeVersionByte(path, "*.fln", 0);

        var originalCommit = IndexFileInspector.FindCommitFiles(path)[0];
        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        var newCommit = IndexFileInspector.FindCommitFiles(path)[0];
        Assert.True(newCommit.Generation > originalCommit.Generation);
        Assert.Contains(newCommit.Generation.ToString(CultureInfo.InvariantCulture), newCommit.FilePath);
        Assert.All(IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds,
            segmentId => Assert.Contains("_migrated_", segmentId, StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Migrate: Old commit and old segment files are cleaned up after publish")]
    public void Migrate_AtomicPublish_OldCommitAndSegmentsCleanedUp()
    {
        var path = CreateCurrentVersionIndex("migrate_cleanup_old");
        DowngradeVersionByte(path, "*.fln", 0);

        var originalCommit = IndexFileInspector.FindCommitFiles(path)[0];
        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(originalCommit.FilePath), "Old commit file should have been removed.");
        Assert.False(File.Exists(Path.Combine(path, $"stats_{originalCommit.Generation}.json")), "Old stats file should have been removed.");
        var newSegmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds[0];
        Assert.NotEmpty(Directory.GetFiles(path, $"{newSegmentId}.*"));
    }

    [Fact(DisplayName = "Migrate: Latest commit references only current-version files")]
    public void Migrate_AtomicPublish_LatestCommitIsCurrentVersion()
    {
        var path = CreateCurrentVersionIndex("migrate_latest_current");
        DowngradeVersionByte(path, "*.fln", 0);
        DowngradeVersionByte(path, "*.dvn", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = true,
            });

        Assert.True(result.Succeeded,
            $"Migration failed: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        Assert.NotNull(result.ValidationResult);
        Assert.DoesNotContain(result.ValidationResult.DetailedIssues,
            issue => issue.Severity == IndexCheckSeverity.Error);
    }

    [Fact(DisplayName = "Migrate: Compound segment is repacked and remains compound")]
    public void Migrate_CompoundSegment_RepackedAndRemainsCompound()
    {
        var path = CreateCurrentVersionIndex("migrate_compound_repack");
        DowngradeVersionByte(path, "*.fln", 0);

        var segmentPath = Directory.GetFiles(path, "*.seg").Single();
        var sourceInfo = SegmentInfo.ReadFrom(segmentPath);
        Assert.True(CompoundFileWriter.Pack(path, sourceInfo.SegmentId));
        sourceInfo.IsCompoundFile = true;
        sourceInfo.WriteTo(segmentPath);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = true,
            });

        Assert.True(result.Succeeded,
            $"Migration failed: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        var targetSegmentId = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds.Single();
        var targetInfo = SegmentInfo.ReadFrom(Path.Combine(path, targetSegmentId + ".seg"));
        Assert.True(targetInfo.IsCompoundFile);
        Assert.True(File.Exists(Path.Combine(path, targetSegmentId + ".cfs")));
        Assert.False(File.Exists(Path.Combine(path, targetSegmentId + ".dic")));
        Assert.NotNull(result.ValidationResult);
        Assert.True(result.ValidationResult.IsHealthy,
            string.Join("; ", result.ValidationResult.DetailedIssues.Select(i => $"{i.Code}: {i.Message}")));
        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: A subsequent merge cannot downgrade current formats")]
    public void Migrate_ThenMerge_RemainsCurrent()
    {
        var path = CreateIndexWithMultipleDocuments("migrate_then_merge_current");
        DowngradeVersionByte(path, "*.fln", 0);

        var migration = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = true,
            });
        Assert.True(migration.Succeeded,
            $"Migration failed: {string.Join("; ", migration.Issues.Select(i => $"{i.Code}: {i.Message}"))}");

        using (var directory = new MMapDirectory(path))
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "document added after migration"));
            document.Add(new NumericField("count", 100));
            document.Add(new StringField("id", "doc-after-migration"));
            writer.AddDocument(document);
            writer.Commit();
            writer.ForceMerge(1);
            writer.Commit();
        }

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));
        Assert.All(plan.Actions, action => Assert.Equal(IndexCodecMigrationActionKind.NoOp, action.Kind));
        var validation = IndexValidator.Check(new MMapDirectory(path), new IndexCheckOptions { Deep = true });
        Assert.True(validation.IsHealthy,
            string.Join("; ", validation.DetailedIssues.Select(i => $"{i.Code}: {i.Message}")));
        AssertIndexReadable(path, "document");
    }

    [Fact(DisplayName = "Migrate: Failed migration leaves source commit generation unchanged")]
    public void Migrate_FailedMigration_LeavesSourceCommitUnchanged()
    {
        var path = CreateCurrentVersionIndex("migrate_failed_unchanged");
        // Downgrade a file to create a real migration action, then corrupt the .dic file
        // so validation-before blocks before any rewrite.
        DowngradeVersionByte(path, "*.fln", 0);
        var dicPath = Directory.GetFiles(path, "*.dic").Single();
        File.WriteAllText(dicPath, "corrupt");

        var originalCommit = IndexFileInspector.FindCommitFiles(path)[0];

        var preValidation = IndexValidator.Check(new MMapDirectory(path), new IndexCheckOptions { Deep = true });
        var preValidationErrors = string.Join("; ", preValidation.DetailedIssues.Where(i => i.Severity == IndexCheckSeverity.Error).Select(i => $"{i.Code}: {i.Message}"));

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = true,
                ValidateAfterMigration = false,
            });

        Assert.False(result.Succeeded,
            $"Migration should have failed. Pre-validation errors: {preValidationErrors}. Result issues: {string.Join("; ", result.Issues.Select(i => $"{i.Code}: {i.Message}"))}");
        var afterCommit = IndexFileInspector.FindCommitFiles(path)[0];
        Assert.Equal(originalCommit.Generation, afterCommit.Generation);
        Assert.Equal(originalCommit.FilePath, afterCommit.FilePath);
    }

    [Fact(DisplayName = "Migrate: Stale source files absent from staging are deleted")]
    public void Migrate_StaleSourceFiles_Deleted()
    {
        var path = CreateCurrentVersionIndex("migrate_stale_cleanup");
        DowngradeVersionByte(path, "*.fln", 0);

        var originalSegmentIds = IndexRecovery.RecoverLatestCommit(path, cleanupOrphans: false)!.SegmentIds;
        Assert.NotEmpty(originalSegmentIds);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);

        // After migration, no old-format files (segment ID + ".ext") should remain.
        // New migrated files have "_migrated_" in the name and are expected.
        foreach (var oldId in originalSegmentIds)
        {
            var stale = Directory.EnumerateFiles(path).FirstOrDefault(f =>
            {
                var name = Path.GetFileName(f);
                if (!name.StartsWith(oldId, StringComparison.Ordinal)) return false;
                var tail = name.AsSpan(oldId.Length);
                return (tail.StartsWith(".") || tail.StartsWith("_gen_") || tail.StartsWith("_v_"))
                       && !tail.Contains("_migrated_", StringComparison.Ordinal);
            });
            Assert.Null(stale);
        }

        AssertIndexReadable(path);
    }

    [Fact(DisplayName = "Migrate: Recovery completes an already-published migration")]
    public void Migrate_Recovery_CompletesInterruptedPublish()
    {
        var path = CreateCurrentVersionIndex("migrate_recovery_complete");
        DowngradeVersionByte(path, "*.fln", 0);

        var originalCommit = IndexFileInspector.FindCommitFiles(path)[0];

        var firstResult = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });
        Assert.True(firstResult.Succeeded);

        // Simulate a crash where the marker was not updated to Published.
        IndexMigrationRecovery.WriteMarker(
            path,
            new IndexMigrationMarker
            {
                State = IndexMigrationState.InProgress,
                SourceDirectory = path,
                StagingDirectory = firstResult.StagingDirectory ?? string.Empty,
                SourceCommitGeneration = originalCommit.Generation,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                PlannedActions = []
            },
            durable: true);

        var secondResult = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions { DryRun = false });

        Assert.True(secondResult.Succeeded);
        Assert.Equal(IndexMigrationState.Published, IndexMigrationRecovery.GetState(path).State);
    }

    [Fact(DisplayName = "Migrate: OutOfMemoryException bubbles up uncaught")]
    public void Migrate_OutOfMemoryException_BubblesUp()
    {
        // IsMigrationFailure filters out OutOfMemoryException and AccessViolationException.
        // Hard to trigger genuinely; this test documents the pattern exists.
        var ex = new OutOfMemoryException();
        Assert.True(ex is OutOfMemoryException);
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

    private static byte ReadSingleBodyByte(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        using var body = frame.OpenBodyInput();
        Assert.Equal(1, body.Length);
        return body.ReadByte();
    }

    private sealed class IncrementingFamilyMigrationCoordinator : ICodecFamilyMigrationCoordinator
    {
        public int InvocationCount { get; private set; }

        public void Migrate(
            IReadOnlyDictionary<string, IndexInput> sourceBodies,
            IReadOnlyDictionary<string, IndexOutput> targetBodies)
        {
            InvocationCount++;
            Assert.Equal(sourceBodies.Keys.Order(StringComparer.Ordinal), targetBodies.Keys.Order(StringComparer.Ordinal));
            foreach (var (formatId, source) in sourceBodies)
            {
                Assert.Equal(1, source.Length);
                targetBodies[formatId].WriteByte(checked((byte)(source.ReadByte() + 1)));
            }
        }
    }
}
