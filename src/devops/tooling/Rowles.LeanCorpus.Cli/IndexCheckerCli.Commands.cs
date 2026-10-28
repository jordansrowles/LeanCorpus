using System.CommandLine;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Cli;

/// <summary>
/// Implements the LeanCorpus command-line tooling.
/// </summary>
public static partial class IndexCheckerCli
{
    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("LeanCorpus command-line tools.");
        root.Add(BuildCheckCommand());
        root.Add(BuildInspectCommand());
        root.Add(BuildCompatCommand());
        root.Add(BuildMigrateCommand());
        root.Add(BuildBackupCommand());
        root.Add(BuildRestoreCommand());
        WorkbenchCommands.AddTo(root);
        WorkbenchCommands.SetRootAction(root);
        return root;
    }

    private static Command BuildCheckCommand()
    {
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var deep = BoolOption("--deep", "Run every deep validation check.");
        var json = BoolOption("--json", "Write JSON instead of text.");
        var postings = BoolOption("--postings", "Deep-check postings.");
        var storedFields = BoolOption("--stored-fields", "Deep-check stored fields.");
        var docValues = BoolOption("--doc-values", "Deep-check DocValues.");
        var vectors = BoolOption("--vectors", "Deep-check vector files.");
        var hnsw = BoolOption("--hnsw", "Deep-check HNSW graph files.");
        var liveDocs = BoolOption("--live-docs", "Deep-check live-doc bitsets.");
        var summaryOnly = BoolOption("--summary-only", "Print only the check summary.");
        var failOnWarnings = BoolOption("--fail-on-warnings", "Return exit code 1 when warnings are found.");
        var outputPath = StringOption("--output", "Write the report to a file.");
        var command = new Command("check", "Validate a LeanCorpus index.");
        command.Add(indexPath);
        command.Add(deep);
        command.Add(json);
        command.Add(postings);
        command.Add(storedFields);
        command.Add(docValues);
        command.Add(vectors);
        command.Add(hnsw);
        command.Add(liveDocs);
        command.Add(summaryOnly);
        command.Add(failOnWarnings);
        command.Add(outputPath);
        command.SetAction(result =>
        {
            var request = CreateRequest(
                GetRequiredValue(result, indexPath),
                result.GetValue(deep),
                result.GetValue(json),
                result.GetValue(postings),
                result.GetValue(storedFields),
                result.GetValue(docValues),
                result.GetValue(vectors),
                result.GetValue(hnsw),
                result.GetValue(liveDocs),
                result.GetValue(summaryOnly),
                result.GetValue(failOnWarnings),
                result.GetValue(outputPath));
            return RunCheck(request, result.InvocationConfiguration.Output, result.InvocationConfiguration.Error);
        });
        return command;
    }

    private static Command BuildInspectCommand()
    {
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var json = BoolOption("--json", "Write JSON instead of text.");
        var outputPath = StringOption("--output", "Write the report to a file.");
        var command = new Command("inspect", "Inspect index format and codec versions.");
        command.Add(indexPath);
        command.Add(json);
        command.Add(outputPath);
        command.SetAction(result => RunInspect(
            GetRequiredValue(result, indexPath),
            result.GetValue(json),
            result.GetValue(outputPath),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));
        return command;
    }

    private static Command BuildCompatCommand()
    {
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var deep = BoolOption("--deep", "Run deep validation before deciding compatibility.");
        var json = BoolOption("--json", "Write JSON instead of text.");
        var outputPath = StringOption("--output", "Write the report to a file.");
        var command = new Command("compat", "Check whether this build can read or write an index.");
        command.Add(indexPath);
        command.Add(deep);
        command.Add(json);
        command.Add(outputPath);
        command.SetAction(result => RunCompat(
            GetRequiredValue(result, indexPath),
            result.GetValue(deep),
            result.GetValue(json),
            result.GetValue(outputPath),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));
        return command;
    }

    private static Command BuildMigrateCommand()
    {
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var dryRun = BoolOption("--dry-run", "Report migration actions without modifying files.");
        var execute = BoolOption("--execute", "Run the migration. Dry-run is used by default.");
        var json = BoolOption("--json", "Write JSON instead of text.");
        var staging = StringOption("--staging", "Use an explicit staging directory.");
        var outputPath = StringOption("--output", "Write the report to a file.");
        var command = new Command("migrate", "Plan or run codec migration.");
        command.Add(indexPath);
        command.Add(dryRun);
        command.Add(execute);
        command.Add(json);
        command.Add(staging);
        command.Add(outputPath);
        command.SetAction(result => RunMigrate(
            GetRequiredValue(result, indexPath),
            !result.GetValue(execute) || result.GetValue(dryRun),
            result.GetValue(staging),
            result.GetValue(json),
            result.GetValue(outputPath),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));
        return command;
    }

    private static Command BuildBackupCommand()
    {
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanCorpus index directory." };
        var backupPath = new Argument<string>("backup-path") { Description = "Path to the backup directory." };
        var commitGeneration = new Option<int?>("--commit-generation") { Description = "Back up a specific commit generation. Latest is used by default." };
        var overwrite = BoolOption("--overwrite", "Allow an existing backup directory to be cleared.");
        var json = BoolOption("--json", "Write JSON instead of text.");
        var outputPath = StringOption("--output", "Write the report to a file.");
        var command = new Command("backup", "Back up a LeanCorpus index commit point.");
        command.Add(indexPath);
        command.Add(backupPath);
        command.Add(commitGeneration);
        command.Add(overwrite);
        command.Add(json);
        command.Add(outputPath);
        command.SetAction(result => RunBackup(
            GetRequiredValue(result, indexPath),
            GetRequiredValue(result, backupPath),
            result.GetValue(commitGeneration),
            result.GetValue(overwrite),
            result.GetValue(json),
            result.GetValue(outputPath),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));
        return command;
    }

    private static Command BuildRestoreCommand()
    {
        var backupPath = new Argument<string>("backup-path") { Description = "Path to the LeanCorpus backup directory." };
        var targetPath = new Argument<string>("target-path") { Description = "Path to the target index directory." };
        var overwrite = BoolOption("--overwrite", "Allow an existing target index directory to be cleared.");
        var skipValidation = BoolOption("--skip-validation", "Skip validation after restore.");
        var json = BoolOption("--json", "Write JSON instead of text.");
        var outputPath = StringOption("--output", "Write the report to a file.");
        var command = new Command("restore", "Restore a LeanCorpus index backup.");
        command.Add(backupPath);
        command.Add(targetPath);
        command.Add(overwrite);
        command.Add(skipValidation);
        command.Add(json);
        command.Add(outputPath);
        command.SetAction(result => RunRestore(
            GetRequiredValue(result, backupPath),
            GetRequiredValue(result, targetPath),
            result.GetValue(overwrite),
            result.GetValue(skipValidation),
            result.GetValue(json),
            result.GetValue(outputPath),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));
        return command;
    }

    private static MMapDirectory OpenDirectory(string indexPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
            throw new ArgumentException("Missing index path.", nameof(indexPath));
        if (!Directory.Exists(indexPath))
            throw new ArgumentException($"Index path '{indexPath}' does not exist.", nameof(indexPath));
        return new MMapDirectory(Path.GetFullPath(indexPath));
    }

    private static Option<bool> BoolOption(string name, string description)
        => new(name) { Description = description };

    private static Option<string?> StringOption(string name, string description)
        => new(name) { Description = description };

    private static string GetRequiredValue(ParseResult result, Argument<string> argument)
        => result.GetValue(argument) ?? throw new ArgumentException($"Missing required argument '{argument.Name}'.");

}
