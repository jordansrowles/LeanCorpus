namespace Rowles.LeanCorpus.DevOps.Diagnostics;

internal enum DiagnosticsOperation
{
    Help,
    ProcessList,
    Counters,
    Trace,
    GcDump,
    Dump,
    Symbols,
    Capture
}

internal sealed record DiagnosticsRequest(
    DiagnosticsOperation Operation,
    int? ProcessId,
    TimeSpan? Duration,
    string DumpType,
    string? ArtifactPath,
    IReadOnlyList<string> PassThrough);

internal sealed record DiagnosticsParseResult(bool Success, DiagnosticsRequest? Request, string? Failure);

internal static class DiagnosticsArgumentParser
{
    public static DiagnosticsParseResult Parse(IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        if (arguments.Count == 0 || IsHelp(arguments[0]))
            return Success(new DiagnosticsRequest(DiagnosticsOperation.Help, null, null, "Mini", null, []));

        var operation = arguments[0].ToLowerInvariant() switch
        {
            "ps" => DiagnosticsOperation.ProcessList,
            "counters" => DiagnosticsOperation.Counters,
            "trace" => DiagnosticsOperation.Trace,
            "gcdump" => DiagnosticsOperation.GcDump,
            "dump" => DiagnosticsOperation.Dump,
            "symbols" => DiagnosticsOperation.Symbols,
            "capture" => DiagnosticsOperation.Capture,
            _ => (DiagnosticsOperation?)null
        };
        if (operation is null)
            return Failure($"Unknown diagnostics command '{arguments[0]}'. Use 'diagnostics --help'.");

        int? processId = null;
        var duration = TimeSpan.FromSeconds(5);
        var durationWasProvided = false;
        var dumpType = "Mini";
        var dumpTypeWasProvided = false;
        var positionals = new List<string>();
        var passThrough = new List<string>();
        var inPassThrough = false;

        for (var index = 1; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (inPassThrough)
            {
                passThrough.Add(argument);
                continue;
            }

            if (argument == "--")
            {
                inPassThrough = true;
                continue;
            }

            if (IsHelp(argument))
                return Success(new DiagnosticsRequest(DiagnosticsOperation.Help, null, null, "Mini", null, []));

            if (!argument.StartsWith("-", StringComparison.Ordinal))
            {
                positionals.Add(argument);
                continue;
            }

            var option = NormalizeOption(argument);
            if (option is not ("pid" or "p" or "duration" or "type"))
                return Failure($"Unknown diagnostics option '{argument}'. Tool options must follow '--'.");
            if (++index >= arguments.Count || IsOptionToken(arguments[index]))
                return Failure($"Diagnostics option '{argument}' requires a value.");

            var value = arguments[index];
            switch (option)
            {
                case "pid":
                case "p":
                    if (!int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var parsedPid) || parsedPid <= 0)
                        return Failure("--pid must be a positive process ID.");
                    processId = parsedPid;
                    break;
                case "duration":
                    if (!TryParseDuration(value, out duration))
                        return Failure("--duration must be a positive value such as 5s, 2m, or 500ms.");
                    durationWasProvided = true;
                    break;
                case "type":
                    dumpType = value.ToLowerInvariant() switch
                    {
                        "mini" => "Mini",
                        "heap" => "Heap",
                        "full" => "Full",
                        "triage" => "Triage",
                        _ => value
                    };
                    dumpTypeWasProvided = true;
                    break;
            }
        }

        if (operation == DiagnosticsOperation.ProcessList)
        {
            if (positionals.Count != 0 || processId is not null || durationWasProvided || dumpTypeWasProvided || passThrough.Count != 0)
                return Failure("The diagnostics 'ps' command does not accept options or additional arguments.");
            return Success(new DiagnosticsRequest(operation.Value, null, null, dumpType, null, []));
        }

        if (operation == DiagnosticsOperation.Symbols)
        {
            if (positionals.Count != 1 || processId is not null || durationWasProvided || dumpTypeWasProvided)
                return Failure("Usage: diagnostics symbols <artifact> [-- <dotnet-symbol arguments>]");
            try
            {
                var artifactPath = Path.GetFullPath(positionals[0], workingDirectory);
                if (!File.Exists(artifactPath))
                    return Failure($"Diagnostic artifact was not found: {artifactPath}");
                return Success(new DiagnosticsRequest(operation.Value, null, null, dumpType, artifactPath, passThrough));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return Failure($"Diagnostic artifact path is invalid: {exception.Message}");
            }
        }

        if (positionals.Count != 0)
            return Failure($"The diagnostics '{operation.Value.ToString().ToLowerInvariant()}' command does not accept positional arguments.");
        if (processId is null)
            return Failure("--pid must be a positive process ID.");
        if (operation == DiagnosticsOperation.Dump && dumpType is not ("Mini" or "Heap" or "Full" or "Triage"))
            return Failure($"Unsupported dump type '{dumpType}'. Expected Mini, Heap, Full, or Triage.");
        if (operation != DiagnosticsOperation.Dump && dumpTypeWasProvided)
            return Failure("--type is only valid for the diagnostics 'dump' command.");
        if (operation != DiagnosticsOperation.Capture && durationWasProvided)
            return Failure("--duration is only valid for the diagnostics 'capture' command.");
        if (operation == DiagnosticsOperation.Capture && duration <= TimeSpan.Zero)
            return Failure("--duration must be greater than zero for diagnostics capture.");

        return Success(new DiagnosticsRequest(operation.Value, processId,
            operation == DiagnosticsOperation.Capture ? duration : null, dumpType, null, passThrough));
    }

    private static string NormalizeOption(string value)
        => value.TrimStart('-').Replace("-", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();

    private static bool IsOptionToken(string value)
        => value == "--" || IsHelp(value) || NormalizeOption(value) is "pid" or "p" or "duration" or "type";

    private static bool IsHelp(string value)
        => value is "-h" or "--help" or "-Help" or "--Help";

    private static bool TryParseDuration(string value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;
        var match = System.Text.RegularExpressions.Regex.Match(value.Trim(), "^(?<number>\\d+(?:\\.\\d+)?)(?<unit>ms|s|m|h|d)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups["number"].Value, System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture, out var number) || number <= 0)
            return false;

        try
        {
            duration = match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "ms" => TimeSpan.FromMilliseconds(number),
                "s" => TimeSpan.FromSeconds(number),
                "m" => TimeSpan.FromMinutes(number),
                "h" => TimeSpan.FromHours(number),
                "d" => TimeSpan.FromDays(number),
                _ => TimeSpan.Zero
            };
            return duration > TimeSpan.Zero && duration <= TimeSpan.MaxValue - TimeSpan.FromSeconds(30);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static DiagnosticsParseResult Success(DiagnosticsRequest request)
        => new(true, request, null);

    private static DiagnosticsParseResult Failure(string message)
        => new(false, null, message);
}
