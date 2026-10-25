using System.Globalization;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Diagnostics;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record TestRunnerOptions(string RepositoryRoot)
{
    public bool ArtifactsEnabled { get; init; }
    public bool Ci { get; init; }
    public bool CollectCoverage { get; init; }
    public bool Diagnostics { get; init; }
    public bool Flaky { get; init; }
    public bool FailWarnings { get; init; }
    public string ParallelProfile { get; init; } = "integration";
    public string ExplicitMode { get; init; } = "off";
    public string Verbosity { get; init; } = string.Empty;
    public string HangTimeout { get; init; } = "100s";
    public IReadOnlyDictionary<string, string?> EnvironmentOverrides { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);
}

internal static class MtpTestArgumentBuilder
{
    public static IReadOnlyList<string> Build(TestTarget target, TestRunnerOptions options, string artifactDirectory)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RepositoryRoot);

        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(target.Filter))
        {
            arguments.AddRange(["--filter", target.Filter]);
        }

        switch (options.Verbosity.Trim().ToLowerInvariant())
        {
            case "quiet":
                arguments.AddRange(["--output", "Normal", "--progress", "off"]);
                break;
            case "detailed":
                arguments.AddRange(["--output", "Detailed"]);
                break;
            case "":
                break;
            default:
                arguments.AddRange(["--output", "Normal"]);
                break;
        }

        if (options.ArtifactsEnabled)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
            arguments.AddRange(["--results-directory", artifactDirectory,
                "--report-xunit-trx", "--report-xunit-trx-filename", "results.trx"]);
        }

        var configurationPath = Path.Combine(options.RepositoryRoot, ".config", "devops", "testconfig.json");
        arguments.AddRange(["--xunit-config-filename", configurationPath]);
        arguments.AddRange(["--parallel", options.ParallelProfile.ToLowerInvariant() switch
        {
            "unit" or "stress" => "all",
            "integration" => "collections",
            _ => throw new InvalidOperationException($"Unknown test profile '{options.ParallelProfile}'. Valid profiles are unit, integration and stress.")
        }]);

        var explicitMode = options.ExplicitMode.Trim().ToLowerInvariant();
        if (explicitMode.Length > 0)
        {
            if (explicitMode is not ("off" or "on" or "only"))
            {
                throw new InvalidOperationException($"Unknown explicit-test mode '{options.ExplicitMode}'. Valid modes are off, on and only.");
            }
            arguments.AddRange(["--explicit", explicitMode]);
        }

        if (options.FailWarnings)
        {
            arguments.AddRange(["--fail-warns", "on"]);
        }
        if (options.Ci)
        {
            arguments.Add("--report-gh");
        }

        if (options.CollectCoverage && target.CoverageEligible)
        {
            arguments.AddRange(["--coverlet", "--coverlet-output-format", "cobertura",
                "--coverlet-file-prefix", $"{target.Suite}-{target.Framework}",
                "--coverlet-exclude-by-file", "**/obj/**/*.cs"]);
        }

        var hasHangDump = TestRunnerCapabilityCatalog.Has(target.Capabilities, TestRunnerCapability.HangDump);
        if (hasHangDump && !options.HangTimeout.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(options.HangTimeout))
            {
                throw new InvalidOperationException("Hang timeout must be a duration or 'off'.");
            }
            arguments.AddRange(["--hangdump", "--hangdump-timeout", options.HangTimeout,
                "--hangdump-type-if-supported", DiagnosticsDumpTypeCatalog.DefaultOption]);
            if (options.ArtifactsEnabled)
            {
                arguments.AddRange(["--hangdump-filename", "hangdump.dmp"]);
            }
        }

        var hasCrashDump = TestRunnerCapabilityCatalog.Has(target.Capabilities, TestRunnerCapability.CrashDump);
        if (hasCrashDump && (options.Flaky || options.Diagnostics || options.Ci))
        {
            arguments.AddRange(["--crashdump", "--crashdump-type", DiagnosticsDumpTypeCatalog.DefaultOption]);
            if (options.ArtifactsEnabled)
            {
                arguments.AddRange(["--crashdump-filename", "crashdump.dmp"]);
            }
        }

        if (options.Diagnostics && options.ArtifactsEnabled)
        {
            var diagnosticsDirectory = Path.Combine(artifactDirectory, "diagnostics");
            Directory.CreateDirectory(diagnosticsDirectory);
            arguments.AddRange(["--diagnostic", "--diagnostic-output-directory", diagnosticsDirectory,
                "--diagnostic-file-prefix", "mtp-", "--diagnostic-verbosity", "Information"]);
        }

        arguments.AddRange(target.AdditionalArguments.Where(static argument => !string.IsNullOrEmpty(argument)));
        return arguments;
    }
}

internal static class TestProcessEnvironmentBuilder
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "LEANCORPUS_TELEMETRY",
        "LEANCORPUS_RUN_ID",
        "LEANCORPUS_RUN_KIND",
        "LEANCORPUS_ARTIFACT_DIR",
        "LEANCORPUS_TARGET",
        "LEANCORPUS_SUITE",
        "LEANCORPUS_ITERATION",
        "LEANCORPUS_CI",
        "LEANCORPUS_DIAGNOSTICS"
    };

    public static string? ValidateOverrides(IReadOnlyDictionary<string, string?>? overrides)
    {
        if (overrides is null)
        {
            return "Test environment overrides cannot be null.";
        }

        foreach (var (name, value) in overrides)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Contains('\0') || name.Contains('\r') || name.Contains('\n'))
            {
                return "Test environment override names must be non-empty and cannot contain '=', NUL or line breaks.";
            }
            if (ReservedNames.Contains(name))
            {
                return $"Test environment variable '{name}' is owned by DevOps and cannot be overridden.";
            }
            if (value?.Contains('\0') == true)
            {
                return $"Test environment override '{name}' cannot contain NUL.";
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string?> Build(
        TestRunnerOptions options,
        Guid runId,
        string? artifactDirectory,
        TestTarget target,
        int iteration)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(target);
        if (ValidateOverrides(options.EnvironmentOverrides) is { } validationError)
        {
            throw new InvalidOperationException(validationError);
        }

        var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["LEANCORPUS_TELEMETRY"] = options.Diagnostics ? "full" : options.Ci || options.Flaky ? "summary" : "off"
        };
        if (options.ArtifactsEnabled)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
            variables["LEANCORPUS_RUN_ID"] = runId.ToString("D");
            variables["LEANCORPUS_RUN_KIND"] = "test";
            variables["LEANCORPUS_ARTIFACT_DIR"] = artifactDirectory;
            variables["LEANCORPUS_TARGET"] = target.Key;
            variables["LEANCORPUS_SUITE"] = target.Suite;
            variables["LEANCORPUS_ITERATION"] = iteration.ToString(CultureInfo.InvariantCulture);
            variables["LEANCORPUS_CI"] = options.Ci ? "true" : "false";
            variables["LEANCORPUS_DIAGNOSTICS"] = options.Diagnostics ? "true" : "false";
        }

        foreach (var (name, value) in options.EnvironmentOverrides)
        {
            variables[name] = value;
        }

        return variables;
    }
}
