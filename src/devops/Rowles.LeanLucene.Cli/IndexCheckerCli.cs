using System.CommandLine;
using System.Text;
using System.Text.Json;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Index.Compatibility;
using Rowles.LeanLucene.Index.Format;
using Rowles.LeanLucene.Index.Migration;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Cli;

/// <summary>
/// Implements the LeanLucene command-line tooling.
/// </summary>
public static class IndexCheckerCli
{
    /// <summary>
    /// Runs the command-line app.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code: 0 for success, 1 for validation or compatibility failures, 2 for invalid arguments or CLI failures.</returns>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    /// Runs the command-line app with redirected text writers.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="output">Standard output writer.</param>
    /// <param name="error">Standard error writer.</param>
    /// <returns>Exit code: 0 for success, 1 for validation or compatibility failures, 2 for invalid arguments or CLI failures.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var root = BuildRootCommand();
        var configuration = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false
        };

        try
        {
            var parseResult = root.Parse(args);
            int exitCode = parseResult.Invoke(configuration);
            return parseResult.Errors.Count > 0 ? CliExitCodes.InvalidArguments : exitCode;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }

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

    internal static CheckRequest CreateRequest(
        string indexPath,
        bool deep,
        bool json,
        bool postings,
        bool storedFields,
        bool docValues,
        bool vectors,
        bool hnsw,
        bool liveDocs,
        bool summaryOnly,
        bool failOnWarnings,
        string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(indexPath))
            throw new ArgumentException("Missing index path.", nameof(indexPath));
        if (!Directory.Exists(indexPath))
            throw new ArgumentException($"Index path '{indexPath}' does not exist.", nameof(indexPath));

        var options = new IndexCheckOptions
        {
            Deep = deep,
            VerifyPostings = postings,
            VerifyStoredFields = storedFields,
            VerifyDocValues = docValues,
            VerifyVectors = vectors,
            VerifyHnsw = hnsw,
            VerifyLiveDocs = liveDocs
        };

        return new CheckRequest(
            Path.GetFullPath(indexPath),
            options,
            json,
            summaryOnly,
            failOnWarnings,
            string.IsNullOrWhiteSpace(outputPath) ? null : Path.GetFullPath(outputPath));
    }

    private static RootCommand BuildRootCommand()
    {
        var root = new RootCommand("LeanLucene command-line tools.");
        root.Add(BuildCheckCommand());
        root.Add(BuildInspectCommand());
        root.Add(BuildCompatCommand());
        root.Add(BuildMigrateCommand());
        return root;
    }

    private static Command BuildCheckCommand()
    {
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanLucene index directory." };
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
        var command = new Command("check", "Validate a LeanLucene index.");
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
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanLucene index directory." };
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
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanLucene index directory." };
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
        var indexPath = new Argument<string>("index-path") { Description = "Path to the LeanLucene index directory." };
        var dryRun = BoolOption("--dry-run", "Report migration actions without modifying files.");
        var execute = BoolOption("--execute", "Run the migration. Dry-run is used by default.");
        var inPlace = BoolOption("--in-place", "Allow migration in the source index directory.");
        var json = BoolOption("--json", "Write JSON instead of text.");
        var staging = StringOption("--staging", "Use an explicit staging directory.");
        var outputPath = StringOption("--output", "Write the report to a file.");
        var command = new Command("migrate", "Plan or run codec migration.");
        command.Add(indexPath);
        command.Add(dryRun);
        command.Add(execute);
        command.Add(inPlace);
        command.Add(json);
        command.Add(staging);
        command.Add(outputPath);
        command.SetAction(result => RunMigrate(
            GetRequiredValue(result, indexPath),
            !result.GetValue(execute) || result.GetValue(dryRun),
            result.GetValue(inPlace),
            result.GetValue(staging),
            result.GetValue(json),
            result.GetValue(outputPath),
            result.InvocationConfiguration.Output,
            result.InvocationConfiguration.Error));
        return command;
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
        bool inPlace,
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
                UseStagingDirectory = !inPlace,
                AllowInPlaceMigration = inPlace,
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
                writer.WriteLine($"  {file.FileName} {file.CodecName} v{file.Version?.ToString() ?? "-"} current={file.CurrentVersion?.ToString() ?? "-"} supported={file.IsSupported}");
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
            _ => throw new InvalidOperationException($"Unsupported JSON DTO type '{typeof(T).Name}'.")
        };
        writer.WriteLine(json);
    }

    private static bool ShouldFail(IndexCheckResult result, bool failOnWarnings)
        => result.DetailedIssues.Any(static issue => issue.Severity == IndexCheckSeverity.Error) ||
           failOnWarnings && result.DetailedIssues.Any(static issue => issue.Severity == IndexCheckSeverity.Warning);
}

internal sealed record CheckRequest(
    string IndexPath,
    IndexCheckOptions Options,
    bool Json,
    bool SummaryOnly,
    bool FailOnWarnings,
    string? OutputPath)
{
    public static CheckRequest Empty { get; } = new(
        string.Empty,
        new IndexCheckOptions(),
        Json: false,
        SummaryOnly: false,
        FailOnWarnings: false,
        OutputPath: null);
}

internal static class CliExitCodes
{
    public const int Success = 0;
    public const int ValidationErrors = 1;
    public const int InvalidArguments = 2;
}

internal sealed class CliIndexCheckResultDto
{
    public required bool IsHealthy { get; init; }
    public int? CommitGeneration { get; init; }
    public required int SegmentsChecked { get; init; }
    public required int DocumentsChecked { get; init; }
    public required int FilesChecked { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }

    public static CliIndexCheckResultDto FromResult(IndexCheckResult result)
    {
        var issues = new List<CliIndexCheckIssueDto>(result.DetailedIssues.Count);
        foreach (var issue in result.DetailedIssues)
            issues.Add(CliIndexCheckIssueDto.FromIssue(issue));

        return new CliIndexCheckResultDto
        {
            IsHealthy = result.IsHealthy,
            CommitGeneration = result.CommitGeneration,
            SegmentsChecked = result.SegmentsChecked,
            DocumentsChecked = result.DocumentsChecked,
            FilesChecked = result.FilesChecked,
            Issues = issues
        };
    }
}

internal sealed class CliIndexCheckIssueDto
{
    public required string Severity { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public string? FileName { get; init; }
    public string? SegmentId { get; init; }
    public required bool IsRepairable { get; init; }
    public required IReadOnlyList<string> SuggestedActions { get; init; }

    public static CliIndexCheckIssueDto FromIssue(IndexCheckIssue issue)
        => new()
        {
            Severity = issue.Severity.ToString(),
            Code = issue.Code,
            Message = issue.Message,
            FileName = issue.FileName,
            SegmentId = issue.SegmentId,
            IsRepairable = issue.IsRepairable,
            SuggestedActions = issue.SuggestedActions
        };
}

internal sealed class CliIndexFormatInventoryDto
{
    public required string DirectoryPath { get; init; }
    public int? CommitGeneration { get; init; }
    public long? ContentToken { get; init; }
    public required IReadOnlyList<string> SegmentIds { get; init; }
    public required List<CliSegmentFormatInventoryDto> Segments { get; init; }
    public required List<CliCodecFileInventoryDto> OrphanFiles { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }
    public required bool HasUnsupportedFutureFormat { get; init; }

    public static CliIndexFormatInventoryDto FromInventory(IndexFormatInventory inventory)
        => new()
        {
            DirectoryPath = inventory.DirectoryPath,
            CommitGeneration = inventory.CommitGeneration,
            ContentToken = inventory.ContentToken,
            SegmentIds = inventory.SegmentIds,
            Segments = inventory.Segments.Select(CliSegmentFormatInventoryDto.FromSegment).ToList(),
            OrphanFiles = inventory.OrphanFiles.Select(CliCodecFileInventoryDto.FromFile).ToList(),
            Issues = inventory.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList(),
            HasUnsupportedFutureFormat = inventory.HasUnsupportedFutureFormat
        };
}

internal sealed class CliSegmentFormatInventoryDto
{
    public required string SegmentId { get; init; }
    public int? DocCount { get; init; }
    public int? LiveDocCount { get; init; }
    public int? CommitGeneration { get; init; }
    public int? DelGeneration { get; init; }
    public required List<CliCodecFileInventoryDto> Files { get; init; }
    public required IReadOnlyList<string> MissingFiles { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }

    public static CliSegmentFormatInventoryDto FromSegment(SegmentFormatInventory segment)
        => new()
        {
            SegmentId = segment.SegmentId,
            DocCount = segment.DocCount,
            LiveDocCount = segment.LiveDocCount,
            CommitGeneration = segment.CommitGeneration,
            DelGeneration = segment.DelGeneration,
            Files = segment.Files.Select(CliCodecFileInventoryDto.FromFile).ToList(),
            MissingFiles = segment.MissingFiles,
            Warnings = segment.Warnings
        };
}

internal sealed class CliCodecFileInventoryDto
{
    public required string FileName { get; init; }
    public required string Extension { get; init; }
    public required string CodecName { get; init; }
    public byte? Version { get; init; }
    public byte? CurrentVersion { get; init; }
    public required bool HasValidMagic { get; init; }
    public required bool IsSupported { get; init; }
    public required bool IsCurrent { get; init; }
    public long? Length { get; init; }
    public string? SegmentId { get; init; }
    public string? FieldName { get; init; }

    public static CliCodecFileInventoryDto FromFile(CodecFileInventory file)
        => new()
        {
            FileName = file.FileName,
            Extension = file.Extension,
            CodecName = file.CodecName,
            Version = file.Version,
            CurrentVersion = file.CurrentVersion,
            HasValidMagic = file.HasValidMagic,
            IsSupported = file.IsSupported,
            IsCurrent = file.IsCurrent,
            Length = file.Length,
            SegmentId = file.SegmentId,
            FieldName = file.FieldName
        };
}

internal sealed class CliCompatibilityResultDto
{
    public required string Status { get; init; }
    public required bool CanRead { get; init; }
    public required bool CanWrite { get; init; }
    public required bool CanValidate { get; init; }
    public required bool CanMigrate { get; init; }
    public required bool MustReject { get; init; }
    public required bool RequiresMigration { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }
    public required List<CliMigrationActionDto> MigrationActions { get; init; }

    public static CliCompatibilityResultDto FromResult(IndexCompatibilityResult result)
        => new()
        {
            Status = result.Status.ToString(),
            CanRead = result.CanRead,
            CanWrite = result.CanWrite,
            CanValidate = result.CanValidate,
            CanMigrate = result.CanMigrate,
            MustReject = result.MustReject,
            RequiresMigration = result.RequiresMigration,
            Issues = result.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList(),
            MigrationActions = result.MigrationActions.Select(CliMigrationActionDto.FromAction).ToList()
        };
}

internal sealed class CliMigrationResultDto
{
    public required bool Succeeded { get; init; }
    public required bool DryRun { get; init; }
    public required string SourceDirectory { get; init; }
    public string? StagingDirectory { get; init; }
    public required List<CliMigrationActionDto> Actions { get; init; }
    public required List<CliIndexCheckIssueDto> Issues { get; init; }

    public static CliMigrationResultDto FromPlan(IndexCodecMigrationPlan plan, string sourceDirectory, string? stagingDirectory)
        => new()
        {
            Succeeded = plan.CanExecute,
            DryRun = true,
            SourceDirectory = sourceDirectory,
            StagingDirectory = stagingDirectory,
            Actions = plan.Actions.Select(CliMigrationActionDto.FromAction).ToList(),
            Issues = plan.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList()
        };

    public static CliMigrationResultDto FromResult(IndexCodecMigrationResult result)
        => new()
        {
            Succeeded = result.Succeeded,
            DryRun = result.DryRun,
            SourceDirectory = result.SourceDirectory,
            StagingDirectory = result.StagingDirectory,
            Actions = result.ExecutedActions.Select(CliMigrationActionDto.FromAction).ToList(),
            Issues = result.Issues.Select(CliIndexCheckIssueDto.FromIssue).ToList()
        };
}

internal sealed class CliMigrationActionDto
{
    public required string Kind { get; init; }
    public required string SourcePath { get; init; }
    public string? TargetPath { get; init; }
    public required string Description { get; init; }
    public required bool CanExecute { get; init; }
    public string? ReasonCannotExecute { get; init; }
    public string? SegmentId { get; init; }
    public string? FileName { get; init; }
    public byte? FromVersion { get; init; }
    public byte? ToVersion { get; init; }

    public static CliMigrationActionDto FromAction(IndexCodecMigrationAction action)
        => new()
        {
            Kind = action.Kind.ToString(),
            SourcePath = action.SourcePath,
            TargetPath = action.TargetPath,
            Description = action.Description,
            CanExecute = action.CanExecute,
            ReasonCannotExecute = action.ReasonCannotExecute,
            SegmentId = action.SegmentId,
            FileName = action.FileName,
            FromVersion = action.FromVersion,
            ToVersion = action.ToVersion
        };
}
