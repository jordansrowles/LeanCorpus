namespace Rowles.DataForge.Tool;

internal sealed record DataForgeHelpRequest(bool Requested, string? Topic = null, string? Error = null);

/// <summary>
/// One help contract compiled into both the managed wrapper and the DataForge executable.
/// </summary>
internal static class DataForgeHelp
{
    private static readonly string[] Commands =
    [
        "profiles",
        "generate",
        "reference",
        "inspect",
        "verify",
        "reproduce"
    ];

    private static readonly string[] ReferenceOperations = ["download", "build", "inspect", "verify"];

    public static DataForgeHelpRequest ParseRequest(IReadOnlyList<string> arguments, bool includeQualification)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
            return new DataForgeHelpRequest(false);

        var terminator = IndexOf(arguments, "--");
        var visibleCount = terminator < 0 ? arguments.Count : terminator;
        var explicitHelp = arguments[0].Equals("help", StringComparison.OrdinalIgnoreCase);
        var helpIndex = -1;
        if (explicitHelp)
        {
            helpIndex = 0;
        }
        else
        {
            for (var index = 0; index < visibleCount; index++)
            {
                if (IsHelpToken(arguments[index]))
                {
                    helpIndex = index;
                    break;
                }
            }
        }

        if (helpIndex < 0)
            return new DataForgeHelpRequest(false);

        string[] commandTokens;
        if (explicitHelp)
        {
            var nestedHelp = -1;
            for (var index = 1; index < visibleCount; index++)
            {
                if (IsHelpToken(arguments[index]))
                {
                    nestedHelp = index;
                    break;
                }
            }
            if (nestedHelp >= 0 && nestedHelp + 1 < visibleCount)
                return Invalid("Help does not accept trailing arguments.");
            commandTokens = arguments.Skip(1).Take((nestedHelp >= 0 ? nestedHelp : visibleCount) - 1).ToArray();
        }
        else
        {
            if (helpIndex + 1 < visibleCount)
                return Invalid("Help options do not accept trailing arguments.");
            commandTokens = arguments.Take(helpIndex).ToArray();
        }

        var commandIndex = Array.FindIndex(commandTokens, static token => !token.StartsWith("-", StringComparison.Ordinal));
        if (commandIndex < 0)
            return new DataForgeHelpRequest(true);

        if (commandTokens.Any(static token => token.StartsWith("-", StringComparison.Ordinal)))
            return Invalid("DataForge help does not accept command options.");

        var command = commandTokens[commandIndex];
        if (command.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            commandIndex++;
            if (commandIndex >= commandTokens.Length)
                return new DataForgeHelpRequest(true);
            command = commandTokens[commandIndex];
        }

        var topic = command.ToLowerInvariant();
        if (topic == "reference")
        {
            var operationIndex = commandIndex + 1;
            if (operationIndex < commandTokens.Length && !commandTokens[operationIndex].StartsWith("-", StringComparison.Ordinal))
            {
                var operation = commandTokens[operationIndex].ToLowerInvariant();
                if (!ReferenceOperations.Contains(operation, StringComparer.Ordinal))
                    return Invalid($"Unknown DataForge reference help topic '{commandTokens[operationIndex]}'.");
                topic += " " + operation;
            }
        }
        else if (!Commands.Contains(topic, StringComparer.Ordinal) &&
                 !(includeQualification && topic == "qualify"))
        {
            return Invalid($"Unknown DataForge help topic '{command}'.");
        }

        if (explicitHelp)
        {
            var allowedTopicCount = topic.StartsWith("reference ", StringComparison.Ordinal) ? 2 : topic == "" ? 0 : 1;
            var positionalCount = commandTokens.Skip(commandIndex).Count(static token => !token.StartsWith("-", StringComparison.Ordinal));
            if (positionalCount > allowedTopicCount)
                return Invalid("Help accepts a command name and, for reference, one operation name.");
        }

        return new DataForgeHelpRequest(true, topic);
    }

    public static void WriteHelp(TextWriter output, string commandPrefix, string? topic, bool includeQualification)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandPrefix);
        if (string.IsNullOrEmpty(topic))
        {
            output.WriteLine("Usage:");
            output.WriteLine($"  {commandPrefix} <command> [options]");
            output.WriteLine("Commands:");
            output.WriteLine("  profiles   List deterministic synthetic profile descriptors.");
            output.WriteLine("  generate   Materialise a deterministic synthetic profile.");
            output.WriteLine("  reference  Download, build, inspect or verify the Wikipedia reference dataset.");
            output.WriteLine("  inspect    Display a dataset manifest.");
            output.WriteLine("  verify     Verify dataset bytes and manifest identity.");
            output.WriteLine("  reproduce  Recreate a generated dataset from its manifest identity.");
            if (includeQualification)
                output.WriteLine("  qualify   Run the managed release qualification workflow.");
            output.WriteLine($"Run '{commandPrefix} <command> --help' for command details.");
            return;
        }

        switch (topic)
        {
            case "profiles":
                output.WriteLine($"Usage: {commandPrefix} profiles [-Json]");
                output.WriteLine("  -Json writes profile descriptors with version, kind, defaults and description.");
                break;
            case "generate":
                output.WriteLine("Usage:");
                output.WriteLine($"  {commandPrefix} generate -Profile leancorpus-search -Version 1 -Seed 42 -Count 20000 [-Output <path>] [-Force]");
                output.WriteLine($"  {commandPrefix} generate -Profile leancorpus-vector -Version 1 -Seed 42 -Count 1000 [-Dimension 64] [-VectorDistribution Uniform] [-ClusterCount 8] [-QueryCount 1] [-Output <path>] [-Force]");
                output.WriteLine($"  {commandPrefix} generate -Profile leancorpus-hybrid -Version 1 -Seed 42 -Count 20000 [-Dimension 64] [-Output <path>] [-Force]");
                output.WriteLine($"  {commandPrefix} generate -Profile leancorpus-stress -Mode search|vector|hybrid [-Version 1] [-Seed 42] [-Count <count>] [-AllowLarge] [-Output <path>] [-Force]");
                output.WriteLine($"  {commandPrefix} generate -Profile rowles-text-multilingual -Language en|fr|de|es|it|pt|nl|ru|ar|zh|ja|ko [-Version 1] [-Seed 42] [-Count 256] [-Output <path>] [-Force]");
                output.WriteLine($"  {commandPrefix} generate -Profile leancorpus-e2e [-Version 1] [-Seed 42] [-Count 256] [-Output <path>] [-Force]");
                output.WriteLine("Use profiles to inspect the registered IDs and defaults.");
                break;
            case "reference":
                output.WriteLine($"Usage: {commandPrefix} reference <download|build|inspect|verify>");
                output.WriteLine("  download [-Cache <path>] [-Force]");
                output.WriteLine("  build [-Cache <path>] [-Output <path>]");
                output.WriteLine("  inspect [-ReferencePath <path>]");
                output.WriteLine("  verify [-ReferencePath <path>]");
                output.WriteLine("These commands operate on the pinned Wikipedia reference dataset.");
                break;
            case "reference download":
                output.WriteLine($"Usage: {commandPrefix} reference download [-Cache <path>] [-Force]");
                output.WriteLine("Downloads the pinned Wikipedia reference dump and index into the local cache.");
                break;
            case "reference build":
                output.WriteLine($"Usage: {commandPrefix} reference build [-Cache <path>] [-Output <path>]");
                output.WriteLine("Builds the deterministic reference dataset from the cached dump.");
                break;
            case "reference inspect":
                output.WriteLine($"Usage: {commandPrefix} reference inspect [-ReferencePath <path>]");
                output.WriteLine("Displays the pinned Wikipedia reference manifest and summaries.");
                break;
            case "reference verify":
                output.WriteLine($"Usage: {commandPrefix} reference verify [-ReferencePath <path>]");
                output.WriteLine("Verifies the pinned Wikipedia reference dataset and manifest.");
                break;
            case "inspect":
                output.WriteLine($"Usage: {commandPrefix} inspect <dataset-dir-or-manifest>");
                break;
            case "verify":
                output.WriteLine($"Usage: {commandPrefix} verify <dataset-dir-or-manifest>");
                break;
            case "reproduce":
                output.WriteLine($"Usage: {commandPrefix} reproduce <dataset-dir-or-manifest>");
                output.WriteLine("Reproduction is available for generated datasets with a registered profile identity.");
                break;
            case "qualify" when includeQualification:
                output.WriteLine($"Usage: {commandPrefix} qualify [--reference-path <path>] [--dry]");
                output.WriteLine("Runs the managed release qualification workflow; --dry prints the planned steps.");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(topic), topic, "The DataForge help topic was not resolved.");
        }
    }

    private static DataForgeHelpRequest Invalid(string message)
        => new(true, Error: message + " Use 'devops dataforge --help' for usage.");

    private static bool IsHelpToken(string value)
        => value.Equals("--help", StringComparison.OrdinalIgnoreCase) || value.Equals("-h", StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
                return index;
        }

        return -1;
    }
}
