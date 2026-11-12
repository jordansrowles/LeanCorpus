namespace Rowles.LeanCorpus.DevOps.Benchmarks;

/// <summary>
/// The single BenchmarkDotNet version policy for run identity and pair assessment (ADR047).
/// </summary>
internal static class BenchmarkToolchainPolicy
{
    /// <summary>The exact BenchmarkDotNet package version benchmark evidence must come from.</summary>
    public const string RequiredBenchmarkDotNetVersion = "0.16.0-preview.2";

    /// <summary>
    /// Accepts the required version, optionally followed by a <c>+</c> build suffix such as the
    /// informational commit, and rejects every other version. Callers keep the original string
    /// as provenance; only the comparison strips the suffix.
    /// </summary>
    public static bool IsAcceptedBenchmarkDotNetVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        string core = plus < 0 ? version : version[..plus];
        string suffix = plus < 0 ? string.Empty : version[(plus + 1)..];
        return string.Equals(core, RequiredBenchmarkDotNetVersion, StringComparison.Ordinal) &&
               (plus < 0 || (suffix.Length > 0 && !suffix.Any(char.IsWhiteSpace)));
    }
}
