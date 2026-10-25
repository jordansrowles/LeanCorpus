using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Cli;

/// <summary>
/// Implements the LeanCorpus command-line tooling.
/// </summary>
public static partial class IndexCheckerCli
{
    internal static int RunCheck(CheckRequest request, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            using var directory = new MMapDirectory(request.IndexPath);
            var result = IndexValidator.Check(directory, request.Options);
            if (request.OutputPath is null)
            {
                if (request.Json)
                    WriteJson(output, CliIndexCheckResultDto.FromResult(result), IndexCheckCliJsonContext.Default.CliIndexCheckResultDto);
                else
                    WriteCheckText(output, result, request.SummaryOnly);
            }
            else
            {
                WriteOutputFile(request.OutputPath, request.Json, CliIndexCheckResultDto.FromResult(result), request.SummaryOnly, result);
                output.WriteLine($"Wrote check result to {request.OutputPath}");
            }

            return ShouldFail(result, request.FailOnWarnings)
                ? CliExitCodes.ValidationErrors
                : CliExitCodes.Success;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

    private static int RunInspect(string indexPath, bool json, string? outputPath, TextWriter output, TextWriter error)
    {
        try
        {
            using var directory = OpenDirectory(indexPath);
            var inventory = IndexFormatInspector.Inspect(directory);
            var dto = CliIndexFormatInventoryDto.FromInventory(inventory);
            WriteCliResult(outputPath, json, output, dto, writer => WriteInspectText(writer, inventory));
            return inventory.HasUnsupportedFutureFormat ? CliExitCodes.ValidationErrors : CliExitCodes.Success;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

    private static int RunCompat(string indexPath, bool deep, bool json, string? outputPath, TextWriter output, TextWriter error)
    {
        try
        {
            using var directory = OpenDirectory(indexPath);
            var result = IndexCompatibility.Check(directory, new IndexCompatibilityOptions { DeepValidation = deep });
            var dto = CliCompatibilityResultDto.FromResult(result);
            WriteCliResult(outputPath, json, output, dto, writer => WriteCompatibilityText(writer, result));
            return result.Status is IndexCompatibilityStatus.Compatible or IndexCompatibilityStatus.Empty or IndexCompatibilityStatus.MigrationRecommended
                ? CliExitCodes.Success
                : CliExitCodes.ValidationErrors;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

    private static int RunMigrate(
        string indexPath,
        bool dryRun,
        string? staging,
        bool json,
        string? outputPath,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            using var directory = OpenDirectory(indexPath);
            var options = new IndexCodecMigrationOptions
            {
                DryRun = dryRun,
                StagingDirectory = string.IsNullOrWhiteSpace(staging) ? null : Path.GetFullPath(staging)
            };
            var result = dryRun
                ? CliMigrationResultDto.FromPlan(IndexCodecMigrator.Plan(directory, options), directory.DirectoryPath, options.StagingDirectory)
                : CliMigrationResultDto.FromResult(IndexCodecMigrator.Migrate(directory, options));
            WriteCliResult(outputPath, json, output, result, writer => WriteMigrationText(writer, result));
            return result.Succeeded ? CliExitCodes.Success : CliExitCodes.ValidationErrors;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

    private static int RunBackup(
        string indexPath,
        string backupPath,
        int? commitGeneration,
        bool overwrite,
        bool json,
        string? outputPath,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            var result = IndexBackup.Backup(
                Path.GetFullPath(indexPath),
                Path.GetFullPath(backupPath),
                new IndexBackupOptions
                {
                    CommitGeneration = commitGeneration,
                    OverwriteBackupDirectory = overwrite
                });
            var dto = CliBackupResultDto.FromResult(result);
            WriteCliResult(outputPath, json, output, dto, writer => WriteBackupText(writer, dto));
            return CliExitCodes.Success;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

    private static int RunRestore(
        string backupPath,
        string targetPath,
        bool overwrite,
        bool skipValidation,
        bool json,
        string? outputPath,
        TextWriter output,
        TextWriter error)
    {
        try
        {
            var result = IndexBackup.Restore(
                Path.GetFullPath(backupPath),
                Path.GetFullPath(targetPath),
                new IndexRestoreOptions
                {
                    OverwriteTargetDirectory = overwrite,
                    ValidateAfterRestore = !skipValidation
                });
            var dto = CliRestoreResultDto.FromResult(result);
            WriteCliResult(outputPath, json, output, dto, writer => WriteRestoreText(writer, dto));
            return dto.IsHealthy ? CliExitCodes.Success : CliExitCodes.ValidationErrors;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

    private static bool ShouldFail(IndexCheckResult result, bool failOnWarnings)
        => result.DetailedIssues.Any(static issue => issue.Severity == IndexCheckSeverity.Error) ||
           failOnWarnings && result.DetailedIssues.Any(static issue => issue.Severity == IndexCheckSeverity.Warning);
}
