using Rowles.LeanCorpus.Cli;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Covers branches and lines in <see cref="IndexCheckerCli"/> not reached by
/// <see cref="IndexCheckerCliTests"/>: null-arg overload, RunCheck catch block,
/// CreateRequest guard, OpenDirectory guard, WriteCliResult output-file branch,
/// WriteOutputFile JSON branch, WriteInspectText, WriteCompatibilityText actions,
/// WriteMigrationText actions, WriteRestoreText, WriteJson migration switch arm,
/// ShouldFail failOnWarnings branch, CliMigrationActionDto.FromAction,
/// and CliRestoreResultDto.FromResult null ValidationResult branch.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Validation")]
public sealed class IndexCheckerCliGapsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCheckerCliGapsTests(TestDirectoryFixture fixture) => _fixture = fixture;

    // ── Single-overload null-arg guard ────────────────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Run(null) throws ArgumentNullException")]
    public void Run_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => IndexCheckerCli.Run(null!));
    }

    // ── RunCheck catch block (lines 90-93) ───────────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: RunCheck with file path returns InvalidArguments")]
    public void RunCheck_FilePath_ReturnsInvalidArguments()
    {
        var filePath = Path.Combine(_fixture.Path, "not_a_directory.txt");
        File.WriteAllText(filePath, "not an index directory");
        var request = new CheckRequest(
            filePath,
            new IndexCheckOptions(),
            Json: false,
            SummaryOnly: false,
            FailOnWarnings: false,
            OutputPath: null);

        using var output = new StringWriter();
        using var error  = new StringWriter();
        int code = IndexCheckerCli.RunCheck(request, output, error);

        Assert.Equal(CliExitCodes.InvalidArguments, code);
        Assert.NotEmpty(error.ToString());
    }

    // ── CreateRequest guards (lines 111-114) ─────────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: CreateRequest with empty path throws ArgumentException")]
    public void CreateRequest_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            IndexCheckerCli.CreateRequest("", false, false, false, false, false, false, false, false, false, false, null));
    }

    [Fact(DisplayName = "IndexCheckerCli: CreateRequest with non-existent path throws ArgumentException")]
    public void CreateRequest_NonExistentPath_Throws()
    {
        var nonExistent = Path.Combine(_fixture.Path, "no_such_dir_" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentException>(() =>
            IndexCheckerCli.CreateRequest(nonExistent, false, false, false, false, false, false, false, false, false, false, null));
    }

    // ── OpenDirectory guards (lines 450-453) ─────────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Inspect empty path returns InvalidArguments")]
    public void Inspect_EmptyPath_ReturnsInvalidArguments()
    {
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["inspect", ""], output, error);

        Assert.Equal(CliExitCodes.InvalidArguments, code);
        Assert.NotEmpty(error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Inspect non-existent path returns InvalidArguments")]
    public void Inspect_NonExistentPath_ReturnsInvalidArguments()
    {
        var nonExistent = Path.Combine(_fixture.Path, "no_such_dir_" + Guid.NewGuid().ToString("N"));
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["inspect", nonExistent], output, error);

        Assert.Equal(CliExitCodes.InvalidArguments, code);
        Assert.NotEmpty(error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Compat non-existent path returns InvalidArguments")]
    public void Compat_NonExistentPath_ReturnsInvalidArguments()
    {
        var nonExistent = Path.Combine(_fixture.Path, "no_such_dir_" + Guid.NewGuid().ToString("N"));
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["compat", nonExistent], output, error);

        Assert.Equal(CliExitCodes.InvalidArguments, code);
        Assert.NotEmpty(error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Migrate non-existent path returns InvalidArguments")]
    public void Migrate_NonExistentPath_ReturnsInvalidArguments()
    {
        var nonExistent = Path.Combine(_fixture.Path, "no_such_dir_" + Guid.NewGuid().ToString("N"));
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["migrate", nonExistent, "--dry-run"], output, error);

        Assert.Equal(CliExitCodes.InvalidArguments, code);
        Assert.NotEmpty(error.ToString());
    }

    // ── WriteInspectText (lines 518-527) ─────────────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Inspect text mode writes segment inventory")]
    public void Inspect_TextOutput_WritesSegmentInventory()
    {
        var path = CreateIndex("cli_gaps_inspect_text");
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["inspect", path], output, error);

        Assert.Equal(0, code);
        var text = output.ToString();
        Assert.Contains("Commit generation:", text);
        Assert.Contains("Segments:", text);
        Assert.Contains("Segment ", text);
        Assert.Empty(error.ToString());
    }

    // ── WriteCliResult output-file branch (lines 477-487) ────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Inspect with output file writes file and confirms")]
    public void Inspect_WithOutputFile_WritesFileAndPrintsConfirmation()
    {
        var path = CreateIndex("cli_gaps_inspect_output");
        var outPath = Path.Combine(_fixture.Path, "inspect-output.txt");
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["inspect", path, "--output", outPath], output, error);

        Assert.Equal(0, code);
        Assert.Contains("Wrote result to", output.ToString());
        Assert.True(File.Exists(outPath));
        Assert.Contains("Commit generation:", File.ReadAllText(outPath));
        Assert.Empty(error.ToString());
    }

    // ── WriteOutputFile JSON branch (line 498) ────────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Check with output file and JSON writes JSON to file")]
    public void Check_WithOutputFileAndJson_WritesJsonToFile()
    {
        var path    = CreateIndex("cli_gaps_check_json_output");
        var outPath = Path.Combine(_fixture.Path, "check-output.json");
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["check", path, "--output", outPath, "--json"], output, error);

        Assert.Equal(0, code);
        Assert.True(File.Exists(outPath));
        Assert.Contains("\"isHealthy\":true", File.ReadAllText(outPath));
        Assert.Empty(error.ToString());
    }

    // ── WriteCompatibilityText migration actions loop (line 543) ─────────────

    [Fact(DisplayName = "IndexCheckerCli: Compat old-version index text output writes migration actions")]
    public void Compat_OldVersionDic_TextOutput_WritesMigrationActions()
    {
        var path = CreateIndex("cli_gaps_compat_actions");
        WriteCodecVersion(path, "*.dic", CodecConstants.TermDictionaryVersion - 1);
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["compat", path], output, error);

        Assert.Equal(0, code);
        var text = output.ToString();
        Assert.Contains("Status: Compatible", text);
        Assert.Empty(error.ToString());
    }

    // ── WriteMigrationText actions loop (line 551) ───────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Migrate dry-run old-version index text output writes actions")]
    public void Migrate_DryRun_OldVersionDic_TextOutput_WritesActions()
    {
        var path = CreateIndex("cli_gaps_migrate_actions");
        WriteCodecVersion(path, "*.dic", CodecConstants.TermDictionaryVersion - 1);
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["migrate", path, "--dry-run"], output, error);

        Assert.Equal(0, code);
        var text = output.ToString();
        Assert.Contains("Migration dry-run", text);
        Assert.Contains("Succeeded: True", text);
        Assert.Empty(error.ToString());
    }

    // ── WriteJson migration switch arm (line 611) ────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Migrate dry-run JSON output writes migration DTO")]
    public void Migrate_DryRun_Json_WritesMigrationDto()
    {
        var path = CreateIndex("cli_gaps_migrate_json");
        WriteCodecVersion(path, "*.dic", CodecConstants.TermDictionaryVersion - 1);
        using var output = new StringWriter();
        using var error  = new StringWriter();

        int code = IndexCheckerCli.Run(["migrate", path, "--dry-run", "--json"], output, error);

        Assert.Equal(0, code);
        var json = output.ToString();
        Assert.Contains("\"dryRun\":true", json);
        Assert.Contains("\"actions\"", json);
        Assert.Empty(error.ToString());
    }

    // ── WriteRestoreText (lines 572-583) ──────────────────────────────────────

    [Fact(DisplayName = "IndexCheckerCli: Restore text output writes restore summary")]
    public void Restore_TextOutput_WritesRestoreSummary()
    {
        var indexPath   = CreateIndex("cli_gaps_restore_src");
        var backupPath  = Path.Combine(_fixture.Path, "cli_gaps_restore_backup");
        var restorePath = Path.Combine(_fixture.Path, "cli_gaps_restore_target");
        using var bOut = new StringWriter(); using var bErr = new StringWriter();
        IndexCheckerCli.Run(["backup", indexPath, backupPath], bOut, bErr);

        using var output = new StringWriter();
        using var error  = new StringWriter();
        int code = IndexCheckerCli.Run(["restore", backupPath, restorePath, "--overwrite"], output, error);

        Assert.Equal(0, code);
        var text = output.ToString();
        Assert.Contains("Restore", text);
        Assert.Contains("Commit generation:", text);
        Assert.Contains("Target directory:", text);
        Assert.Contains("Healthy:", text);
        Assert.Empty(error.ToString());
    }

    // ── CliRestoreResultDto null ValidationResult branch (line 902) ──────────

    [Fact(DisplayName = "IndexCheckerCli: Restore with skip-validation reports IsHealthy=true")]
    public void Restore_SkipValidation_IsHealthyTrue()
    {
        var indexPath   = CreateIndex("cli_gaps_restore_skipval_src");
        var backupPath  = Path.Combine(_fixture.Path, "cli_gaps_restore_skipval_backup");
        var restorePath = Path.Combine(_fixture.Path, "cli_gaps_restore_skipval_target");
        using var bOut = new StringWriter(); using var bErr = new StringWriter();
        IndexCheckerCli.Run(["backup", indexPath, backupPath], bOut, bErr);

        using var output = new StringWriter();
        using var error  = new StringWriter();
        int code = IndexCheckerCli.Run(
            ["restore", backupPath, restorePath, "--overwrite", "--skip-validation", "--json"],
            output, error);

        Assert.Equal(0, code);
        Assert.Contains("\"isHealthy\":true", output.ToString());
        Assert.Empty(error.ToString());
    }

    // ── ShouldFail failOnWarnings fourth branch (line 620) ───────────────────

    [Fact(DisplayName = "IndexCheckerCli: Check with fail-on-warnings and stale temp file returns ValidationErrors")]
    public void Check_FailOnWarnings_WithStaleTempFile_ReturnsValidationErrors()
    {
        var path = CreateIndex("cli_gaps_fail_on_warnings");
        // Add a recognised stale temp file so IndexValidator emits a Warning issue.
        File.WriteAllBytes(Path.Combine(path, "segments_1.tmp"), [0x00]);

        using var output = new StringWriter();
        using var error  = new StringWriter();
        int code = IndexCheckerCli.Run(["check", path, "--fail-on-warnings"], output, error);

        Assert.Equal(CliExitCodes.ValidationErrors, code);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string CreateIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var dir    = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();
        return path;
    }

    private static void WriteCodecVersion(string indexPath, string pattern, int version)
    {
        var path = Directory.GetFiles(indexPath, pattern).Single();
        using var stream = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = sizeof(int);
        stream.WriteByte((byte)version);
    }
}
