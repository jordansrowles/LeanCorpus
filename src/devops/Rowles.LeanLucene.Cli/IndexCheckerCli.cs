using System.Text;
using System.Text.Json;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Store;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Rowles.LeanLucene.Cli;

/// <summary>
/// Implements the LeanLucene command-line checker.
/// </summary>
public static class IndexCheckerCli
{
    /// <summary>
    /// Runs the Spectre.Console.Cli command app.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code: 0 for healthy, 1 for validation errors, 2 for invalid arguments or CLI failures.</returns>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName("leanlucene-cli.exe");
            config.SetApplicationVersion(typeof(IndexCheckerCli).Assembly.GetName().Version?.ToString() ?? "1.3.0");
            config.AddCommand<CheckCommand>("check")
                .WithDescription("Validate a LeanLucene index.");
            config.AddCommand<InteractiveCommand>("interactive")
                .WithDescription("Choose an index and validation depth interactively.");
        });

        int exitCode = app.Run(args);
        return exitCode < 0 ? CliExitCodes.InvalidArguments : exitCode;
    }

    /// <summary>
    /// Runs the command-line checker with redirected text writers.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="output">Standard output writer.</param>
    /// <param name="error">Standard error writer.</param>
    /// <returns>Exit code: 0 for healthy, 1 for validation errors, 2 for invalid arguments or CLI failures.</returns>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                WriteHelp(output);
                return 0;
            }

            if (string.Equals(args[0], "interactive", StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine("Interactive mode requires a terminal. Use the executable directly.");
                return CliExitCodes.InvalidArguments;
            }

            if (!string.Equals(args[0], "check", StringComparison.OrdinalIgnoreCase))
            {
                error.WriteLine($"Unknown command '{args[0]}'.");
                WriteHelp(error);
                return CliExitCodes.InvalidArguments;
            }

            if (args.Length == 2 && IsHelp(args[1]))
            {
                WriteHelp(output);
                return 0;
            }

            if (!TryParseCheckArguments(args, error, out var request))
                return CliExitCodes.InvalidArguments;

            return RunCheck(request, output, error);
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
                    WriteJson(output, result);
                else
                    WriteText(output, result, request.SummaryOnly);
            }
            else
            {
                WriteOutputFile(request.OutputPath, request.Json, result, request.SummaryOnly);
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

    internal static int RunCheckWithSpectre(CheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var directory = new MMapDirectory(request.IndexPath);
            var result = IndexValidator.Check(directory, request.Options);
            if (request.OutputPath is not null)
            {
                WriteOutputFile(request.OutputPath, request.Json, result, request.SummaryOnly);
                AnsiConsole.MarkupLine($"[green]Wrote check result to[/] [grey]{Markup.Escape(request.OutputPath)}[/]");
            }
            else if (request.Json)
            {
                WriteJson(Console.Out, result);
            }
            else
            {
                WriteSpectreText(result, request.SummaryOnly);
            }

            return ShouldFail(result, request.FailOnWarnings)
                ? CliExitCodes.ValidationErrors
                : CliExitCodes.Success;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
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

    private static bool TryParseCheckArguments(string[] args, TextWriter error, out CheckRequest request)
    {
        request = CheckRequest.Empty;
        string indexPath = string.Empty;
        string? outputPath = null;
        bool deep = false;
        bool json = false;
        bool postings = false;
        bool storedFields = false;
        bool docValues = false;
        bool vectors = false;
        bool hnsw = false;
        bool liveDocs = false;
        bool summaryOnly = false;
        bool failOnWarnings = false;

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (IsHelp(arg))
            {
                error.WriteLine("Help must be requested as 'leanlucene-cli.exe check --help'.");
                return false;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                switch (arg)
                {
                    case "--deep":
                        deep = true;
                        break;
                    case "--json":
                        json = true;
                        break;
                    case "--postings":
                        postings = true;
                        break;
                    case "--stored-fields":
                        storedFields = true;
                        break;
                    case "--doc-values":
                        docValues = true;
                        break;
                    case "--vectors":
                        vectors = true;
                        break;
                    case "--hnsw":
                        hnsw = true;
                        break;
                    case "--live-docs":
                        liveDocs = true;
                        break;
                    case "--summary-only":
                        summaryOnly = true;
                        break;
                    case "--fail-on-warnings":
                        failOnWarnings = true;
                        break;
                    case "--output":
                        if (i + 1 >= args.Length)
                        {
                            error.WriteLine("--output requires a value.");
                            return false;
                        }

                        i++;
                        outputPath = args[i];
                        break;
                    default:
                        error.WriteLine($"Unknown option '{arg}'.");
                        return false;
                }
                continue;
            }

            if (!string.IsNullOrEmpty(indexPath))
            {
                error.WriteLine("Only one index path can be supplied.");
                return false;
            }

            indexPath = arg;
        }

        try
        {
            request = CreateRequest(
                indexPath,
                deep,
                json,
                postings,
                storedFields,
                docValues,
                vectors,
                hnsw,
                liveDocs,
                summaryOnly,
                failOnWarnings,
                outputPath);
            return true;
        }
        catch (ArgumentException ex)
        {
            error.WriteLine(ex.Message);
            return false;
        }
    }

    private static bool IsHelp(string arg)
        => string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase);

    private static void WriteHelp(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  leanlucene-cli.exe check <index-path> [--deep] [--json] [--postings] [--stored-fields] [--doc-values] [--vectors] [--hnsw] [--live-docs] [--summary-only] [--fail-on-warnings] [--output <path>]");
        writer.WriteLine("  leanlucene-cli.exe interactive");
    }

    private static void WriteText(TextWriter writer, IndexCheckResult result, bool summaryOnly)
    {
        writer.WriteLine(FormatSummary(result));
        if (summaryOnly)
            return;

        foreach (var issue in result.DetailedIssues)
            writer.WriteLine(FormatIssue(issue));
    }

    private static void WriteSpectreText(IndexCheckResult result, bool summaryOnly)
    {
        AnsiConsole.Write(new Panel(Markup.Escape(FormatSummary(result)))
            .Header(result.IsHealthy ? "Healthy" : "Unhealthy")
            .BorderColor(result.IsHealthy ? Color.Green : Color.Red));

        if (summaryOnly || result.DetailedIssues.Count == 0)
            return;

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Severity")
            .AddColumn("Code")
            .AddColumn("Segment")
            .AddColumn("File")
            .AddColumn("Repairable")
            .AddColumn("Message");

        foreach (var issue in result.DetailedIssues)
        {
            string severityColour = issue.Severity switch
            {
                IndexCheckSeverity.Error => "red",
                IndexCheckSeverity.Warning => "yellow",
                _ => "grey"
            };

            table.AddRow(
                $"[{severityColour}]{issue.Severity}[/]",
                Markup.Escape(issue.Code),
                Markup.Escape(issue.SegmentId ?? "-"),
                Markup.Escape(issue.FileName ?? "-"),
                issue.IsRepairable ? "[green]yes[/]" : "[grey]no[/]",
                Markup.Escape(issue.Message));
        }

        AnsiConsole.Write(table);
    }

    private static string FormatSummary(IndexCheckResult result)
        => result.IsHealthy
            ? $"Healthy: checked {result.SegmentsChecked} segment(s), {result.DocumentsChecked} document(s), {result.FilesChecked} file(s)."
            : $"Unhealthy: checked {result.SegmentsChecked} segment(s), {result.DocumentsChecked} document(s), {result.FilesChecked} file(s).";

    private static string FormatIssue(IndexCheckIssue issue)
        => $"{issue.Severity} {issue.Code} {issue.SegmentId ?? "-"} {issue.FileName ?? "-"} {issue.Message}";

    private static void WriteOutputFile(string outputPath, bool json, IndexCheckResult result, bool summaryOnly)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var writer = new StreamWriter(outputPath, append: false, Encoding.UTF8);
        if (json)
            WriteJson(writer, result);
        else
            WriteText(writer, result, summaryOnly);
    }

    private static void WriteJson(TextWriter writer, IndexCheckResult result)
    {
        var dto = CliIndexCheckResultDto.FromResult(result);
        var json = JsonSerializer.Serialize(dto, IndexCheckCliJsonContext.Default.CliIndexCheckResultDto);
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

    public static CliIndexCheckIssueDto FromIssue(IndexCheckIssue issue)
        => new()
        {
            Severity = issue.Severity.ToString(),
            Code = issue.Code,
            Message = issue.Message,
            FileName = issue.FileName,
            SegmentId = issue.SegmentId,
            IsRepairable = issue.IsRepairable
        };
}
