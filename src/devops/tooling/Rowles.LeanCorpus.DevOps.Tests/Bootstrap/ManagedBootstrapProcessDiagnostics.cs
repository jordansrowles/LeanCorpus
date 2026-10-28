using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.DevOps.Tests.Bootstrap;

internal static partial class ManagedBootstrapProcessDiagnostics
{
    private const int MaximumCapturedCharacters = 16 * 1024;

    [GeneratedRegex(@"(?im)^\s*authorization\s*:\s*.*$", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeoutMilliseconds)]
    private static partial Regex AuthorizationHeader();

    [GeneratedRegex(@"(?i)(?<name>\b(?:password|passwd|token|secret|api[_-]?key)\b\s*[:=]\s*)[^\s,;]+", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeoutMilliseconds)]
    private static partial Regex SensitiveAssignment();

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9_]{10,}|github_pat_[A-Za-z0-9_]{10,})\b", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeoutMilliseconds)]
    private static partial Regex GitHubToken();

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeoutMilliseconds)]
    private static partial Regex AnsiEscape();

    public static string Format(
        int exitCode,
        string command,
        string workingDirectory,
        string fakeBinPath,
        string commandResolution,
        string environmentSummary,
        string standardOutput,
        string standardError)
    {
        var output = new StringBuilder();
        output.AppendLine("Managed bootstrap child failure diagnostics");
        output.Append("Command: ").AppendLine(RedactAndBound(command));
        output.Append("Exit code: ").AppendLine(exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
        output.Append("OS/RID: ").Append(RuntimeInformation.OSDescription).Append(" / ")
            .Append(RuntimeInformation.RuntimeIdentifier).Append(" / ")
            .AppendLine(RuntimeInformation.ProcessArchitecture.ToString());
        output.Append("Working directory: ").AppendLine(RedactAndBound(workingDirectory));
        output.AppendLine("Relevant environment:").AppendLine(RedactAndBound(environmentSummary));
        output.AppendLine("Fake-bin inventory:").AppendLine(RedactAndBound(DescribeFakeBin(fakeBinPath)));
        output.AppendLine("Get-Command git/dotnet resolution:").AppendLine(RedactAndBound(commandResolution));

        var firstException = FindFirstPowerShellException(standardError, standardOutput);
        output.AppendLine("First PowerShell exception:")
            .AppendLine(firstException is null ? "<none captured>" : RedactAndBound(firstException));
        output.AppendLine("Captured stdout:").AppendLine(RedactAndBound(standardOutput));
        output.AppendLine("Captured stderr:").AppendLine(RedactAndBound(standardError));
        return output.ToString();
    }

    public static string CreateEnvironmentSummary(IDictionary<string, string?> environment)
    {
        // Record only bootstrap path inputs; never serialise the process environment.
        string[] names =
        [
            "PATH",
            "HOME",
            "USERPROFILE",
            "DOTNET_ROOT",
            "DOTNET_ROOT_X64",
            "DOTNET_BUNDLE_EXTRACT_BASE_DIR"
        ];

        return string.Join(Environment.NewLine, names.Select(name =>
        {
            var value = environment.TryGetValue(name, out var configuredValue) ? configuredValue : null;
            return $"{name}={value ?? "<unset>"}";
        }));
    }

    private static string DescribeFakeBin(string path)
    {
        if (!Directory.Exists(path))
        {
            return $"{path}: <missing>";
        }

        var entries = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(path).Order(StringComparer.Ordinal))
        {
            try
            {
                var info = new FileInfo(entry);
                var attributes = File.GetAttributes(entry);
                var details = $"{Path.GetFileName(entry)}; attributes={attributes}";
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    details += $"; linkTarget={info.LinkTarget ?? "<unavailable>"}";
                }
                else if (!info.Attributes.HasFlag(FileAttributes.Directory))
                {
                    details += $"; bytes={info.Length}";
                    if (!OperatingSystem.IsWindows())
                    {
                        try
                        {
                            details += $"; mode={File.GetUnixFileMode(entry)}";
                        }
                        catch (IOException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                    }
                }

                entries.Add(details);
            }
            catch (IOException exception)
            {
                entries.Add($"{Path.GetFileName(entry)}; inventoryError={exception.GetType().Name}");
            }
            catch (UnauthorizedAccessException exception)
            {
                entries.Add($"{Path.GetFileName(entry)}; inventoryError={exception.GetType().Name}");
            }
        }

        return entries.Count == 0 ? "<empty>" : string.Join(Environment.NewLine, entries);
    }

    private static string? FindFirstPowerShellException(string standardError, string standardOutput)
    {
        var text = AnsiEscape().Replace(standardError + Environment.NewLine + standardOutput, string.Empty);
        var lines = text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var index = Array.FindIndex(lines, line =>
            line.Contains("Exception:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("ParserError:", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("NativeCommandError", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("FullyQualifiedErrorId", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        return string.Join(Environment.NewLine, lines.Skip(index).Take(10)).Trim();
    }

    private static string RedactAndBound(string value)
    {
        var redacted = AuthorizationHeader().Replace(value, "Authorization: <redacted>");
        redacted = SensitiveAssignment().Replace(redacted, match => $"{match.Groups["name"].Value}<redacted>");
        redacted = GitHubToken().Replace(redacted, "<redacted-token>");
        if (redacted.Length <= MaximumCapturedCharacters)
        {
            return redacted;
        }

        const string marker = "\n...[output truncated]...\n";
        var retainedCharacters = MaximumCapturedCharacters - marker.Length;
        var prefixLength = retainedCharacters / 2;
        var suffixLength = retainedCharacters - prefixLength;
        var omittedCharacters = redacted.Length - retainedCharacters;
        return string.Concat(
            redacted.AsSpan(0, prefixLength),
            marker,
            $"[{omittedCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture)} characters omitted]\n",
            redacted.AsSpan(redacted.Length - suffixLength));
    }
}
