using System.Globalization;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record TestRunOptions
{
    public TestTargetRequest Selection { get; init; } = new();
    public int? Count { get; init; }
    public bool Flaky { get; init; }
    public bool FailFast { get; init; }
    public bool Diagnostics { get; init; }
    public bool NoRestore { get; init; }
    public bool Explicit { get; init; }
    public bool ExplicitOnly { get; init; }
    public bool FailWarnings { get; init; }
    public bool UiEvents { get; init; }
    public string Profile { get; init; } = string.Empty;
    public string Verbosity { get; init; } = string.Empty;
    public string HangTimeout { get; init; } = "100s";
    public string ProcessTimeout { get; init; } = "off";
    public string Commit { get; init; } = string.Empty;
    public string Range { get; init; } = string.Empty;
    public string CommandLine { get; init; } = "devops test";
    public IReadOnlyDictionary<string, string?> EnvironmentOverrides { get; init; }
        = new Dictionary<string, string?>(StringComparer.Ordinal);
}

internal sealed record TestSelectionPlan(
    IReadOnlyList<TestTarget> Targets,
    AffectedTestSelection? AffectedSelection);

internal sealed class TestRunPlanner(DevOpsConfiguration configuration, AffectedSelectionService affectedSelection)
{
    public async Task<TestExecutorRequest> PlanAsync(
        CheckoutIdentity checkout,
        TestRunOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(options);
        var selection = await ResolveSelectionAsync(checkout, options.Selection, options.Commit, options.Range,
            cancellationToken).ConfigureAwait(false);
        var count = options.Count ?? (options.Flaky ? 30 : 1);
        if (count < 1)
        {
            throw new InvalidOperationException("Test repetition count must be a positive integer.");
        }
        var profile = options.Profile.Length > 0 ? options.Profile.ToLowerInvariant()
            : options.Selection.Category.Equals("Unit", StringComparison.OrdinalIgnoreCase) ? "unit"
            : options.Flaky ? "stress" : "integration";
        if (profile is not ("unit" or "integration" or "stress"))
        {
            throw new InvalidOperationException($"Unknown test profile '{options.Profile}'. Valid profiles are unit, integration and stress.");
        }
        _ = ParseDuration(options.HangTimeout, "hang timeout", allowEmpty: false);
        var processTimeout = ParseDuration(options.ProcessTimeout, "process timeout", allowEmpty: true);
        _ = checked(selection.Targets.Count * count);
        return new TestExecutorRequest
        {
            Checkout = checkout,
            Targets = selection.Targets,
            AffectedSelection = selection.AffectedSelection,
            Count = count,
            FailFast = options.FailFast,
            NoRestore = options.NoRestore,
            ProcessTimeout = processTimeout,
            CommandLine = options.CommandLine,
            RunnerOptions = new TestRunnerOptions(checkout.RepositoryRoot)
            {
                ArtifactsEnabled = selection.Targets.Any(static target => target.RunnerKind.Equals("Mtp", StringComparison.OrdinalIgnoreCase)) ||
                    count > 1 || options.Flaky || options.Diagnostics || options.Selection.ContinuousIntegration ||
                    options.Selection.CollectCoverage || options.UiEvents,
                Ci = options.Selection.ContinuousIntegration,
                CollectCoverage = options.Selection.CollectCoverage,
                Diagnostics = options.Diagnostics,
                Flaky = options.Flaky,
                FailWarnings = options.FailWarnings,
                ParallelProfile = profile,
                ExplicitMode = options.ExplicitOnly ? "only" : options.Explicit ? "on" : "off",
                Verbosity = options.Verbosity,
                HangTimeout = options.HangTimeout.ToLowerInvariant(),
                EnvironmentOverrides = options.EnvironmentOverrides
            }
        };
    }

    public async Task<TestSelectionPlan> ResolveSelectionAsync(
        CheckoutIdentity checkout,
        TestTargetRequest request,
        string commit = "",
        string range = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(request);
        var suites = request.Suites
            .SelectMany(static value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        var isAffected = suites.Length == 1 && suites[0].Equals("affected", StringComparison.OrdinalIgnoreCase);
        if (commit.Length > 0 && range.Length > 0)
        {
            throw new InvalidOperationException("Commit and range are mutually exclusive.");
        }
        if (!isAffected && (commit.Length > 0 || range.Length > 0))
        {
            throw new InvalidOperationException("Commit and range are valid only for the affected suite.");
        }

        AffectedTestSelection? affected = null;
        if (isAffected)
        {
            affected = await affectedSelection.ResolveAsync(checkout.RepositoryRoot, configuration.TestSuites,
                configuration.CodeAreas, commit, range, cancellationToken).ConfigureAwait(false);
        }
        var targets = new TestTargetResolver(configuration.TestSuites).Resolve(request, affected?.AreasBySuite);
        return new TestSelectionPlan(targets, affected);
    }

    private static TimeSpan? ParseDuration(string value, string name, bool allowEmpty)
    {
        if (value.Equals("off", StringComparison.OrdinalIgnoreCase) || allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        var match = Regex.Match(value.Trim(), @"^(?<number>\d+(?:\.\d+)?)(?<unit>ms|s|m|h|d)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !double.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var number))
        {
            throw new InvalidOperationException($"The {name} must be a duration such as 30s, 2m or 500ms, or off.");
        }
        try
        {
            return match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "ms" => TimeSpan.FromMilliseconds(number),
                "s" => TimeSpan.FromSeconds(number),
                "m" => TimeSpan.FromMinutes(number),
                "h" => TimeSpan.FromHours(number),
                _ => TimeSpan.FromDays(number)
            };
        }
        catch (OverflowException exception)
        {
            throw new InvalidOperationException($"The {name} exceeds the supported duration range.", exception);
        }
    }
}
