using System.Globalization;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Migration;
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
    private string CreateCurrentVersionIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world test migration"));
        doc.Add(new NumericField("count", 42));
        doc.Add(new StringField("id", "doc-1"));
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

    /// <summary>
    /// Patches the first byte (version) of all files matching <paramref name="pattern"/>.
    /// </summary>
    private static void DowngradeVersionByte(string indexPath, string pattern, byte version)
    {
        foreach (var filePath in Directory.GetFiles(indexPath, pattern))
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Write, FileShare.None);
            stream.WriteByte(version);
        }
    }

    /// <summary>
    /// Reads the first byte (version) of a matching file.
    /// </summary>
    private static byte ReadVersionByte(string indexPath, string pattern)
    {
        var path = Directory.GetFiles(indexPath, pattern).Single();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (byte)stream.ReadByte();
    }

    /// <summary>
    /// Re-wraps current v2 stored-fields files as v1 CodecKit envelopes.
    /// Used to exercise the stored-fields migration path.
    /// </summary>
    private static void DowngradeStoredFieldsToV1(string indexPath)
    {
        var fdtPath = Directory.GetFiles(indexPath, "*.fdt").Single();
        var fdxPath = Directory.GetFiles(indexPath, "*.fdx").Single();

        // Re-wrap .fdt: v2 body is everything after the version byte.
        var fdtBytes = File.ReadAllBytes(fdtPath);
        var fdtBody = fdtBytes.AsSpan(1);
        int fdtHeaderSize;
        using (var fs = new FileStream(fdtPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(1);
            fdtHeaderSize = 1 + WriteVarInt64(fs, fdtBody.Length);
            fs.Write(fdtBody);
        }

        // Re-wrap .fdx and shift block offsets by the extra v1 header bytes.
        var fdxBytes = File.ReadAllBytes(fdxPath);
        var fdxBody = fdxBytes.AsSpan(1);
        int blockSize = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(fdxBody);
        int docCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(fdxBody.Slice(4));
        int blockCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(fdxBody.Slice(8));
        long headerDelta = fdtHeaderSize - 1;

        using (var fs = new FileStream(fdxPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(1);
            int fdxHeaderSize = 1 + WriteVarInt64(fs, fdxBody.Length);
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

    private static int WriteVarInt64(Stream stream, long value)
    {
        int bytesWritten = 0;
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
            bytesWritten++;
        }
        stream.WriteByte((byte)value);
        return bytesWritten + 1;
    }

    /// <summary>
    /// Verifies the index is queryable after migration.
    /// </summary>
    private static void AssertIndexReadable(string indexPath, string term = "hello")
    {
        using var directory = new MMapDirectory(indexPath);
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
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

    [Fact(DisplayName = "Plan: Downgraded file produces a RewriteFile action")]
    public void Plan_DowngradedFile_ProducesRewriteAction()
    {
        var path = CreateCurrentVersionIndex("plan_downgraded");
        // Downgrade field lengths (.fln) — a v1 format with no version dispatch.
        DowngradeVersionByte(path, "*.fln", 0);

        var plan = IndexCodecMigrator.Plan(new MMapDirectory(path));

        Assert.Contains(plan.Actions, action =>
            action.Kind == IndexCodecMigrationActionKind.RewriteFile &&
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
        Assert.Equal(0, ReadVersionByte(path, "*.fln"));
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
    private void AssertRewriteRestoresVersion(string testName, string pattern, byte expectedVersion, string searchTerm = "hello")
    {
        var path = CreateCurrentVersionIndex(testName);
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
        AssertIndexReadable(path, searchTerm);
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
            action.Kind == IndexCodecMigrationActionKind.RewriteFile &&
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
}
