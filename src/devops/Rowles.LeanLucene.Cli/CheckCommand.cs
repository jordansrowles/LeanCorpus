using System.ComponentModel;
using Spectre.Console.Cli;

namespace Rowles.LeanLucene.Cli;

internal sealed class CheckCommand : Command<CheckCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<index-path>")]
        [Description("Path to the LeanLucene index directory.")]
        public string IndexPath { get; init; } = string.Empty;

        [CommandOption("--deep")]
        [Description("Run every deep validation check.")]
        public bool Deep { get; init; }

        [CommandOption("--json")]
        [Description("Write JSON instead of the formatted text report.")]
        public bool Json { get; init; }

        [CommandOption("--postings")]
        [Description("Deep-check postings.")]
        public bool Postings { get; init; }

        [CommandOption("--stored-fields")]
        [Description("Deep-check stored fields.")]
        public bool StoredFields { get; init; }

        [CommandOption("--doc-values")]
        [Description("Deep-check DocValues.")]
        public bool DocValues { get; init; }

        [CommandOption("--vectors")]
        [Description("Deep-check vector files.")]
        public bool Vectors { get; init; }

        [CommandOption("--hnsw")]
        [Description("Deep-check HNSW graph files.")]
        public bool Hnsw { get; init; }

        [CommandOption("--live-docs")]
        [Description("Deep-check live-doc bitsets.")]
        public bool LiveDocs { get; init; }

        [CommandOption("--summary-only")]
        [Description("Print only the check summary.")]
        public bool SummaryOnly { get; init; }

        [CommandOption("--fail-on-warnings")]
        [Description("Return exit code 1 when warnings are found.")]
        public bool FailOnWarnings { get; init; }

        [CommandOption("--output <path>")]
        [Description("Write the report to a file.")]
        public string? OutputPath { get; init; }
    }

    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var request = IndexCheckerCli.CreateRequest(
                settings.IndexPath,
                settings.Deep,
                settings.Json,
                settings.Postings,
                settings.StoredFields,
                settings.DocValues,
                settings.Vectors,
                settings.Hnsw,
                settings.LiveDocs,
                settings.SummaryOnly,
                settings.FailOnWarnings,
                settings.OutputPath);
            return IndexCheckerCli.RunCheckWithSpectre(request);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return CliExitCodes.InvalidArguments;
        }
    }
}
