using Rowles.LeanCorpus.Cli;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

[Trait("Category", "Index")]
[Trait("Category", "Validation")]
public sealed class IndexCheckerCliTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexCheckerCliTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "IndexCheckerCli: Check Valid Index Returns Zero")]
    public void IndexCheckerCli_CheckValidIndex_ReturnsZero()
    {
        var path = CreateIndex("cli_valid");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Healthy", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Check Corrupt Index Returns One")]
    public void IndexCheckerCli_CheckCorruptIndex_ReturnsOne()
    {
        var path = CreateIndex("cli_corrupt");
        File.Delete(Directory.GetFiles(path, "*.dic").Single());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path], output, error);

        Assert.Equal(1, exitCode);
        Assert.Contains(IndexCheckIssueCodes.RequiredFileMissing, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Json Writes Stable Shape")]
    public void IndexCheckerCli_CheckJson_WritesStableJson()
    {
        var path = CreateIndex("cli_json");
        File.Delete(Directory.GetFiles(path, "*.dic").Single());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path, "--json"], output, error);

        Assert.Equal(1, exitCode);
        var json = output.ToString();
        Assert.Contains("\"isHealthy\":false", json);
        Assert.Contains("\"issues\"", json);
        Assert.Contains("\"suggestedActions\"", json);
        Assert.Contains(IndexCheckIssueCodes.RequiredFileMissing, json);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Invalid Arguments Return Two")]
    public void IndexCheckerCli_InvalidArguments_ReturnsTwo()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", "--unknown"], output, error);

        Assert.Equal(2, exitCode);
        Assert.NotEqual(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Output Writes Report File")]
    public void IndexCheckerCli_Output_WritesReportFile()
    {
        var path = CreateIndex("cli_output");
        var outputPath = Path.Combine(_fixture.Path, "cli-output-report.txt");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["check", path, "--output", outputPath, "--summary-only"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Wrote check result", output.ToString());
        Assert.Contains("Healthy", File.ReadAllText(outputPath));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Inspect Json Writes Inventory")]
    public void IndexCheckerCli_InspectJson_WritesInventory()
    {
        var path = CreateIndex("cli_inspect");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["inspect", path, "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"commitGeneration\":1", output.ToString());
        Assert.Contains("\"segments\"", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Compat Valid Index Returns Zero")]
    public void IndexCheckerCli_CompatValidIndex_ReturnsZero()
    {
        var path = CreateIndex("cli_compat");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["compat", path], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Status: Compatible", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Compat Json Writes Safety Flags")]
    public void IndexCheckerCli_CompatJson_WritesSafetyFlags()
    {
        var path = CreateIndex("cli_compat_json");
        WriteCodecVersion(path, "*.dic", CodecConstants.TermDictionaryVersion + 1);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["compat", path, "--json"], output, error);

        Assert.Equal(1, exitCode);
        var json = output.ToString();
        Assert.Contains($"\"status\":\"{IndexCompatibilityStatus.UnsupportedFutureFormat}\"", json);
        Assert.Contains("\"canValidate\":false", json);
        Assert.Contains("\"mustReject\":true", json);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Migrate Dry Run Returns Plan")]
    public void IndexCheckerCli_MigrateDryRun_ReturnsPlan()
    {
        var path = CreateIndex("cli_migrate");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["migrate", path, "--dry-run"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Migration dry-run", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Migrate Dry Run Overrides Execute")]
    public void IndexCheckerCli_MigrateDryRun_OverridesExecute()
    {
        var path = CreateIndex("cli_migrate_dry_run_wins");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["migrate", path, "--execute", "--dry-run"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Migration dry-run", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Migrate Execute Runs Migration")]
    public void IndexCheckerCli_MigrateExecute_RunsMigration()
    {
        var path = CreateIndex("cli_migrate_execute");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["migrate", path, "--execute"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Migration", output.ToString());
        Assert.DoesNotContain("Migration dry-run", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Backup Writes Manifest")]
    public void IndexCheckerCli_Backup_WritesManifest()
    {
        var path = CreateIndex("cli_backup");
        var backupPath = Path.Combine(_fixture.Path, "cli-backup-output");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["backup", path, backupPath], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("Backup", output.ToString());
        Assert.True(File.Exists(Path.Combine(backupPath, "leancorpus-backup-manifest.json")));
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Backup Json Writes Manifest Shape")]
    public void IndexCheckerCli_BackupJson_WritesManifestShape()
    {
        var path = CreateIndex("cli_backup_json");
        var backupPath = Path.Combine(_fixture.Path, "cli-backup-json-output");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = IndexCheckerCli.Run(["backup", path, backupPath, "--json"], output, error);

        Assert.Equal(0, exitCode);
        Assert.Contains("\"commitGeneration\":1", output.ToString());
        Assert.Contains("\"files\"", output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Restore Recreates Healthy Index")]
    public void IndexCheckerCli_Restore_RecreatesHealthyIndex()
    {
        var path = CreateIndex("cli_restore");
        var backupPath = Path.Combine(_fixture.Path, "cli-restore-backup");
        var restorePath = Path.Combine(_fixture.Path, "cli-restore-output");
        using var backupOutput = new StringWriter();
        using var backupError = new StringWriter();
        using var restoreOutput = new StringWriter();
        using var restoreError = new StringWriter();

        int backupExitCode = IndexCheckerCli.Run(["backup", path, backupPath], backupOutput, backupError);
        int restoreExitCode = IndexCheckerCli.Run(["restore", backupPath, restorePath, "--json"], restoreOutput, restoreError);

        Assert.Equal(0, backupExitCode);
        Assert.Equal(0, restoreExitCode);
        Assert.Contains("\"isHealthy\":true", restoreOutput.ToString());
        Assert.True(File.Exists(Path.Combine(restorePath, "segments_1")));
        Assert.Equal(string.Empty, restoreError.ToString());
    }

    [Fact(DisplayName = "IndexCheckerCli: Restore Corrupt Backup Returns Two")]
    public void IndexCheckerCli_RestoreCorruptBackup_ReturnsTwo()
    {
        var path = CreateIndex("cli_restore_corrupt");
        var backupPath = Path.Combine(_fixture.Path, "cli-restore-corrupt-backup");
        var restorePath = Path.Combine(_fixture.Path, "cli-restore-corrupt-output");
        using var backupOutput = new StringWriter();
        using var backupError = new StringWriter();
        using var restoreOutput = new StringWriter();
        using var restoreError = new StringWriter();

        int backupExitCode = IndexCheckerCli.Run(["backup", path, backupPath], backupOutput, backupError);
        File.AppendAllText(Directory.GetFiles(backupPath, "*.dic").Single(), "corruption");
        int restoreExitCode = IndexCheckerCli.Run(["restore", backupPath, restorePath], restoreOutput, restoreError);

        Assert.Equal(0, backupExitCode);
        Assert.Equal(2, restoreExitCode);
        Assert.NotEqual(string.Empty, restoreError.ToString());
    }

    private string CreateIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var dir = new MMapDirectory(path);
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
