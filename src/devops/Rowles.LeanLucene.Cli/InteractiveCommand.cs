using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Rowles.LeanLucene.Cli;

internal sealed class InteractiveCommand : Command<InteractiveCommand.Settings>
{
    private const string Postings = "Postings";
    private const string StoredFields = "Stored fields";
    private const string DocValues = "DocValues";
    private const string Vectors = "Vectors";
    private const string Hnsw = "HNSW";
    private const string LiveDocs = "Live docs";

    public sealed class Settings : CommandSettings
    {
        [CommandOption("--index <path>")]
        [Description("Initial index path.")]
        public string? IndexPath { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        string indexPath = settings.IndexPath ?? AnsiConsole.Ask<string>("Index path:");
        if (!Directory.Exists(indexPath))
        {
            AnsiConsole.MarkupLine($"[red]Index path '{Markup.Escape(indexPath)}' does not exist.[/]");
            return CliExitCodes.InvalidArguments;
        }

        bool deep = AnsiConsole.Confirm("Run every deep validation check?", defaultValue: false);
        IReadOnlyList<string> selections = deep ? [] : AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select targeted deep checks")
                .NotRequired()
                .PageSize(7)
                .AddChoices(Postings, StoredFields, DocValues, Vectors, Hnsw, LiveDocs));

        bool json = AnsiConsole.Confirm("Write JSON output?", defaultValue: false);
        bool summaryOnly = !json && AnsiConsole.Confirm("Show summary only?", defaultValue: false);
        bool failOnWarnings = AnsiConsole.Confirm("Fail on warnings?", defaultValue: false);
        string? outputPath = AnsiConsole.Confirm("Write the report to a file?", defaultValue: false)
            ? AnsiConsole.Ask<string>("Output path:")
            : null;

        var request = IndexCheckerCli.CreateRequest(
            indexPath,
            deep,
            json,
            selections.Contains(Postings, StringComparer.Ordinal),
            selections.Contains(StoredFields, StringComparer.Ordinal),
            selections.Contains(DocValues, StringComparer.Ordinal),
            selections.Contains(Vectors, StringComparer.Ordinal),
            selections.Contains(Hnsw, StringComparer.Ordinal),
            selections.Contains(LiveDocs, StringComparer.Ordinal),
            summaryOnly,
            failOnWarnings,
            outputPath);

        return IndexCheckerCli.RunCheckWithSpectre(request);
    }
}
