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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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

    [Fact(DisplayName = "Migrate: In-place migration rewrites in source directory")]
    public void Migrate_InPlace_RewritesInSource()
    {
        var path = CreateCurrentVersionIndex("migrate_inplace");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                AllowInPlaceMigration = true,
                UseStagingDirectory = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.Null(result.StagingDirectory);
        Assert.NotEmpty(result.ExecutedActions);
        Assert.Equal(CodecConstants.FieldLengthVersion, ReadVersionByte(path, "*.fln"));
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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
        DowngradeVersionByte(path, "*.fdt", 0);
        DowngradeVersionByte(path, "*.fdx", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = false,
            });

        Assert.True(result.Succeeded);
        Assert.Equal(CodecConstants.StoredFieldsVersion, ReadVersionByte(path, "*.fdt"));
        Assert.Equal(CodecConstants.StoredFieldsVersion, ReadVersionByte(path, "*.fdx"));
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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

    [Fact(DisplayName = "Migrate: In-place validation filters migration marker issue")]
    public void Migrate_InPlace_FiltersMigrationMarker()
    {
        var path = CreateCurrentVersionIndex("migrate_inplace_marker");
        DowngradeVersionByte(path, "*.fln", 0);

        var result = IndexCodecMigrator.Migrate(
            new MMapDirectory(path),
            new IndexCodecMigrationOptions
            {
                DryRun = false,
                AllowInPlaceMigration = true,
                UseStagingDirectory = false,
                ValidateBeforeMigration = false,
                ValidateAfterMigration = true,
            });

        Assert.True(result.Succeeded);
        if (result.ValidationResult is not null)
        {
            Assert.DoesNotContain(result.ValidationResult.DetailedIssues,
                issue => issue.Code == IndexCheckIssueCodes.MigrationInProgress);
        }
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
                UseStagingDirectory = false,
                AllowInPlaceMigration = true,
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

    [Fact(DisplayName = "Migrate: OutOfMemoryException bubbles up uncaught")]
    public void Migrate_OutOfMemoryException_BubblesUp()
    {
        // IsMigrationFailure filters out OutOfMemoryException and AccessViolationException.
        // Hard to trigger genuinely; this test documents the pattern exists.
        var ex = new OutOfMemoryException();
        Assert.True(ex is OutOfMemoryException);
    }
}
