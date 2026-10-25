using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Index.Format;
using Rowles.LeanCorpus.Index.Migration;

namespace Rowles.LeanCorpus.Cli;

/// <summary>
/// Implements the LeanCorpus command-line tooling.
/// </summary>
public static partial class IndexCheckerCli
{
    private static void WriteCliResult<T>(string? outputPath, bool json, TextWriter output, T dto, Action<TextWriter> writeText)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            if (json)
                WriteJson(output, dto);
            else
                writeText(output);
            return;
        }

        var fullPath = Path.GetFullPath(outputPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(fullPath, append: false, Encoding.UTF8);
        if (json)
            WriteJson(writer, dto);
        else
            writeText(writer);
        output.WriteLine($"Wrote result to {fullPath}");
    }

    private static void WriteOutputFile(string outputPath, bool json, CliIndexCheckResultDto dto, bool summaryOnly, IndexCheckResult result)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
        if (json)
            WriteJson(writer, dto, IndexCheckCliJsonContext.Default.CliIndexCheckResultDto);
        else
            WriteCheckText(writer, result, summaryOnly);
    }

    private static void WriteCheckText(TextWriter writer, IndexCheckResult result, bool summaryOnly)
    {
        writer.WriteLine(FormatSummary(result));
        if (summaryOnly)
            return;

        foreach (var issue in result.DetailedIssues)
        {
            writer.WriteLine(FormatIssue(issue));
            WriteSuggestedActions(writer, issue, indent: "  ");
        }
    }

    private static void WriteInspectText(TextWriter writer, IndexFormatInventory inventory)
    {
        writer.WriteLine($"Index: {inventory.DirectoryPath}");
        writer.WriteLine($"Commit generation: {inventory.CommitGeneration?.ToString() ?? "-"}");
        writer.WriteLine($"Segments: {inventory.Segments.Count}");
        foreach (var segment in inventory.Segments)
        {
            writer.WriteLine($"Segment {segment.SegmentId}: docs={segment.DocCount?.ToString() ?? "-"}, live={segment.LiveDocCount?.ToString() ?? "-"}, files={segment.Files.Count}");
            foreach (var file in segment.Files)
            {
                writer.WriteLine($"  {file.FileName}");
                writer.WriteLine($"    format: {file.FormatId ?? file.CodecName}");
                writer.WriteLine($"    frame: {file.FrameVersion?.ToString() ?? file.FrameKind.ToString()}");
                writer.WriteLine($"    format-version: {file.FormatVersion?.ToString() ?? "-"}");
                writer.WriteLine($"    current: {file.IsCurrent}");
                writer.WriteLine($"    checksum: {file.ChecksumAlgorithm?.ToString() ?? "none"}");
                writer.WriteLine($"    checksum-status: {file.ChecksumStatus}");
                writer.WriteLine($"    location: {file.PhysicalLocation}");
            }
        }
    }

    private static void WriteCompatibilityText(TextWriter writer, IndexCompatibilityResult result)
    {
        writer.WriteLine($"Status: {result.Status}");
        writer.WriteLine($"Can read: {result.CanRead}");
        writer.WriteLine($"Can write: {result.CanWrite}");
        writer.WriteLine($"Can validate: {result.CanValidate}");
        writer.WriteLine($"Can migrate: {result.CanMigrate}");
        writer.WriteLine($"Must reject: {result.MustReject}");
        foreach (var issue in result.Issues)
        {
            writer.WriteLine(FormatIssue(issue));
            WriteSuggestedActions(writer, issue, indent: "  ");
        }
        foreach (var action in result.MigrationActions)
            writer.WriteLine($"{action.Kind} {action.FileName ?? "-"} {action.Description}");
    }

    private static void WriteMigrationText(TextWriter writer, CliMigrationResultDto result)
    {
        writer.WriteLine(result.DryRun ? "Migration dry-run" : "Migration");
        writer.WriteLine($"Succeeded: {result.Succeeded}");
        foreach (var action in result.Actions)
            writer.WriteLine($"{action.Kind} {action.FileName ?? "-"} {action.Description}");
        foreach (var issue in result.Issues)
        {
            writer.WriteLine($"{issue.Severity} {issue.Code} {issue.Message}");
            foreach (var suggestedAction in issue.SuggestedActions)
                writer.WriteLine($"  Suggested action: {suggestedAction}");
        }
    }

    private static void WriteBackupText(TextWriter writer, CliBackupResultDto result)
    {
        writer.WriteLine("Backup");
        writer.WriteLine($"Commit generation: {result.CommitGeneration}");
        writer.WriteLine($"Backup directory: {result.BackupDirectoryPath}");
        writer.WriteLine($"Files: {result.Files.Count}");
        foreach (var file in result.Files)
            writer.WriteLine($"{file.FileName} {file.Role} length={file.Length} crc32={file.Crc32}");
    }

    private static void WriteRestoreText(TextWriter writer, CliRestoreResultDto result)
    {
        writer.WriteLine("Restore");
        writer.WriteLine($"Commit generation: {result.CommitGeneration}");
        writer.WriteLine($"Target directory: {result.TargetDirectoryPath}");
        writer.WriteLine($"Files: {result.RestoredFiles.Count}");
        writer.WriteLine($"Healthy: {result.IsHealthy}");
        foreach (var issue in result.Issues)
        {
            writer.WriteLine($"{issue.Severity} {issue.Code} {issue.Message}");
            foreach (var suggestedAction in issue.SuggestedActions)
                writer.WriteLine($"  Suggested action: {suggestedAction}");
        }
    }

    private static string FormatSummary(IndexCheckResult result)
        => result.IsHealthy
            ? $"Healthy: checked {result.SegmentsChecked} segment(s), {result.DocumentsChecked} document(s), {result.FilesChecked} file(s)."
            : $"Unhealthy: checked {result.SegmentsChecked} segment(s), {result.DocumentsChecked} document(s), {result.FilesChecked} file(s).";

    private static string FormatIssue(IndexCheckIssue issue)
        => $"{issue.Severity} {issue.Code} {issue.SegmentId ?? "-"} {issue.FileName ?? "-"} {issue.Message}";

    private static void WriteSuggestedActions(TextWriter writer, IndexCheckIssue issue, string indent)
    {
        foreach (var suggestedAction in issue.SuggestedActions)
            writer.WriteLine($"{indent}Suggested action: {suggestedAction}");
    }

    private static void WriteJson<T>(TextWriter writer, T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> jsonTypeInfo)
    {
        var json = JsonSerializer.Serialize(value, jsonTypeInfo);
        writer.WriteLine(json);
    }

    private static void WriteJson<T>(TextWriter writer, T value)
    {
        var json = value switch
        {
            CliIndexFormatInventoryDto inventory => JsonSerializer.Serialize(inventory, IndexCheckCliJsonContext.Default.CliIndexFormatInventoryDto),
            CliCompatibilityResultDto compatibility => JsonSerializer.Serialize(compatibility, IndexCheckCliJsonContext.Default.CliCompatibilityResultDto),
            CliMigrationResultDto migration => JsonSerializer.Serialize(migration, IndexCheckCliJsonContext.Default.CliMigrationResultDto),
            CliBackupResultDto backup => JsonSerializer.Serialize(backup, IndexCheckCliJsonContext.Default.CliBackupResultDto),
            CliRestoreResultDto restore => JsonSerializer.Serialize(restore, IndexCheckCliJsonContext.Default.CliRestoreResultDto),
            _ => throw new InvalidOperationException($"Unsupported JSON DTO type '{typeof(T).Name}'.")
        };
        writer.WriteLine(json);
    }

}
