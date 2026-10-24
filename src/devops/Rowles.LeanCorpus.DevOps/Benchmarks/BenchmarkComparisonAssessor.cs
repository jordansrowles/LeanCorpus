using System.Globalization;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record BenchmarkComparisonEvidence(
    string SourceCommit,
    bool SourceDirty,
    BenchmarkHostIdentity Host,
    IReadOnlyList<BenchmarkCaseConfiguration> CaseFamilies,
    IReadOnlyList<BenchmarkComparisonSourceReport> Reports);

internal sealed record BenchmarkComparisonSourceReport(
    int? SchemaVersion,
    string? SourceCommit,
    bool? SourceDirty,
    string? DatasetFingerprintSha256,
    IReadOnlyList<string> DatasetProfiles,
    string? HostMachineName,
    string? OsDescription,
    string? RuntimeIdentifier,
    string? ProcessArchitecture,
    string? RuntimeFramework,
    string? BenchmarkDotNetVersion,
    IReadOnlyList<BenchmarkComparisonCase> Rows)
{
    public string? ReadFailure { get; init; }

    public static BenchmarkComparisonSourceReport Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = document.RootElement;
            JsonElement provenance = TryGetObject(root, "provenance");
            var datasetProfiles = ReadDatasetProfiles(provenance);
            var rows = new List<BenchmarkComparisonCase>();
            if (root.TryGetProperty("suites", out JsonElement suites) && suites.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement suite in suites.EnumerateArray())
                {
                    if (!suite.TryGetProperty("benchmarks", out JsonElement benchmarks) || benchmarks.ValueKind != JsonValueKind.Array)
                        continue;
                    rows.AddRange(benchmarks.EnumerateArray().Where(static row => row.ValueKind == JsonValueKind.Object).Select(ReadCase));
                }
            }

            return new BenchmarkComparisonSourceReport(
                GetInt32(root, "schemaVersion"),
                GetString(provenance, "sourceCommit"),
                GetBoolean(provenance, "gitDirty"),
                GetString(provenance, "dataFingerprintSha256"),
                datasetProfiles,
                GetString(root, "hostMachineName"),
                GetString(provenance, "osDescription"),
                GetString(provenance, "runtimeIdentifier"),
                GetString(provenance, "processArchitecture"),
                GetString(provenance, "runtimeFramework"),
                GetString(provenance, "benchmarkDotNetVersion"),
                rows);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new BenchmarkComparisonSourceReport(null, null, null, null, [], null, null, null, null, null, null, [])
            {
                ReadFailure = exception.Message
            };
        }
    }

    private static IReadOnlyList<string> ReadDatasetProfiles(JsonElement provenance)
    {
        if (!provenance.TryGetProperty("datasets", out JsonElement datasets) || datasets.ValueKind != JsonValueKind.Array)
            return [];

        var profiles = new SortedSet<string>(StringComparer.Ordinal);
        foreach (JsonElement dataset in datasets.EnumerateArray())
        {
            string? profileId = GetString(dataset, "profileId");
            int? profileVersion = GetInt32(dataset, "profileVersion");
            if (string.IsNullOrWhiteSpace(profileId))
                continue;
            string versionedId = profileVersion is { } version
                ? profileId.EndsWith($"-v{version.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal)
                    ? profileId
                    : $"{profileId}-v{version.ToString(CultureInfo.InvariantCulture)}"
                : profileId;
            profiles.Add(versionedId);
        }

        return profiles.ToArray();
    }

    private static BenchmarkComparisonCase ReadCase(JsonElement row)
    {
        JsonElement statistics = TryGetObject(row, "statistics");
        return new BenchmarkComparisonCase(
            GetString(row, "typeName") ?? string.Empty,
            GetString(row, "methodName") ?? string.Empty,
            ReadStringMap(row, "parameters"),
            GetString(row, "resultState") ?? string.Empty,
            GetBoolean(row, "identityComplete"),
            GetString(row, "runtimeName"),
            GetString(row, "runtimeVersion"),
            GetString(row, "toolchainType"),
            GetString(row, "toolchainRuntimeName"),
            GetString(row, "toolchainRuntimeVersion"),
            ReadStringMap(row, "jobSettings"),
            ReadStringMap(row, "environmentVariables"),
            GetInt32(statistics, "sampleCount"),
            GetInt32(row, "actualWorkloadMeasurementCount"),
            GetDouble(statistics, "coefficientOfVariationPercent"),
            GetDouble(statistics, "meanNanoseconds"),
            ReadStringArray(row, "qualityFlags"),
            GetBoolean(row, "preflightPassed"));
    }

    private static JsonElement TryGetObject(JsonElement value, string propertyName)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.Object
            ? property
            : default;

    private static string? GetString(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out JsonElement property))
            return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int? GetInt32(JsonElement value, string propertyName)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out JsonElement property) &&
           property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int result)
            ? result
            : null;

    private static double? GetDouble(JsonElement value, string propertyName)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out JsonElement property) &&
           property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out double result)
            ? result
            : null;

    private static bool? GetBoolean(JsonElement value, string propertyName)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out JsonElement property) &&
           property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    private static Dictionary<string, string> ReadStringMap(JsonElement value, string propertyName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Object)
            return result;
        foreach (JsonProperty item in property.EnumerateObject())
        {
            string? itemValue = item.Value.ValueKind switch
            {
                JsonValueKind.String => item.Value.GetString(),
                JsonValueKind.Number => item.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
            if (itemValue is not null)
                result[item.Name] = itemValue;
        }
        return result;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind != JsonValueKind.Array)
            return [];
        return property.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()!)
            .ToArray();
    }
}

internal sealed record BenchmarkComparisonCase(
    string TypeName,
    string MethodName,
    IReadOnlyDictionary<string, string> Parameters,
    string ResultState,
    bool? IdentityComplete,
    string? RuntimeName,
    string? RuntimeVersion,
    string? ToolchainType,
    string? ToolchainRuntimeName,
    string? ToolchainRuntimeVersion,
    IReadOnlyDictionary<string, string> JobSettings,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    int? SampleCount,
    int? ActualWorkloadMeasurementCount,
    double? CoefficientOfVariationPercent,
    double? MeanNanoseconds,
    IReadOnlyList<string> QualityFlags,
    bool? PreflightPassed);

internal sealed record BenchmarkComparisonAssessment(
    string QualificationState,
    bool EligibleForRatio,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<BenchmarkComparisonPairAssessment> Comparisons);

internal sealed record BenchmarkComparisonPairAssessment(
    string ComparisonGroup,
    string ParameterKey,
    string LeftFamilyId,
    string LeftEngineId,
    string RightFamilyId,
    string RightEngineId,
    string QualificationState,
    bool EligibleForRatio,
    double? MeanRatio,
    IReadOnlyList<string> ReasonCodes);

internal static class BenchmarkComparisonAssessor
{
    private const int CurrentSchemaVersion = 5;
    private const int MinimumSamples = 30;
    private const double MaximumCoefficientOfVariationPercent = 5d;
    private const string RequiredBenchmarkDotNetVersion = "0.16.0-preview.2";

    public static BenchmarkComparisonAssessment Assess(BenchmarkComparisonEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.Host);
        ArgumentNullException.ThrowIfNull(evidence.CaseFamilies);
        ArgumentNullException.ThrowIfNull(evidence.Reports);

        var comparisons = new List<BenchmarkComparisonPairAssessment>();
        foreach (var group in evidence.CaseFamilies
                     .Where(static family => family.Project == "core" && family.ComparisonGroup is not null && IsParity(family.Classification))
                     .GroupBy(static family => family.ComparisonGroup!, StringComparer.Ordinal)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            BenchmarkCaseConfiguration[] families = group.OrderBy(static family => family.EngineId, StringComparer.Ordinal).ToArray();
            List<MatchedCase>[] matched = families.Select(family => FindMatches(family, evidence.Reports)).ToArray();
            var variants = matched.SelectMany(static cases => cases)
                .Select(static item => item.ParameterKey)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();
            if (variants.Length == 0)
                continue;

            foreach (string variant in variants)
            {
                var reasons = new SortedSet<string>(StringComparer.Ordinal);
                if (families.Length != 2 || families.Select(static family => family.EngineId).Distinct(StringComparer.Ordinal).Count() != 2)
                    reasons.Add("ComparisonGroupMustHaveTwoEngines");
                ValidateFamilyParity(families, reasons);

                MatchedCase[][] selected = matched
                    .Select(cases => cases.Where(item => item.ParameterKey == variant).ToArray())
                    .ToArray();
                if (selected.Length != 2)
                {
                    reasons.Add("ComparisonGroupMustHaveTwoEngines");
                    comparisons.Add(BuildPair(group.Key, variant, families, null, null, reasons));
                    continue;
                }

                foreach (MatchedCase[] familyCases in selected)
                {
                    if (familyCases.Length == 0)
                        reasons.Add("MissingCaseRows");
                    else if (familyCases.Length > 1)
                        reasons.Add("DuplicateCaseRows");
                }

                if (selected.Any(static familyCases => familyCases.Length != 1))
                {
                    comparisons.Add(BuildPair(group.Key, variant, families,
                        selected[0].Length == 1 ? selected[0][0] : null,
                        selected[1].Length == 1 ? selected[1][0] : null,
                        reasons));
                    continue;
                }

                MatchedCase left = selected[0][0];
                MatchedCase right = selected[1][0];
                ValidatePair(evidence, families[0], families[1], left, right, reasons);
                comparisons.Add(BuildPair(group.Key, variant, families, left, right, reasons));
            }
        }

        if (comparisons.Count == 0)
        {
            return new BenchmarkComparisonAssessment("Unqualified", false, ["NoPairedRows"], []);
        }

        bool eligible = comparisons.All(static comparison => comparison.EligibleForRatio);
        string state = eligible ? "Qualified" : "Unqualified";
        var reasonCodes = comparisons.SelectMany(static comparison => comparison.ReasonCodes)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static reason => reason, StringComparer.Ordinal)
            .ToArray();
        return new BenchmarkComparisonAssessment(state, eligible, reasonCodes, comparisons);
    }

    private static List<MatchedCase> FindMatches(
        BenchmarkCaseConfiguration family,
        IReadOnlyList<BenchmarkComparisonSourceReport> reports)
    {
        var result = new List<MatchedCase>();
        foreach (BenchmarkComparisonSourceReport report in reports)
        {
            foreach (BenchmarkComparisonCase row in report.Rows)
            {
                if (!MatchesFamily(row, family))
                    continue;
                result.Add(new MatchedCase(row, report, CanonicalParameters(row.Parameters)));
            }
        }
        return result;
    }

    private static bool MatchesFamily(BenchmarkComparisonCase row, BenchmarkCaseConfiguration family)
    {
        string shortTypeName = row.TypeName.Split('.').Last();
        if (!shortTypeName.Equals(family.BenchmarkClass, StringComparison.Ordinal) ||
            !(family.BenchmarkMethods.Contains(row.MethodName, StringComparer.Ordinal) || family.MethodAliases.Contains(row.MethodName, StringComparer.Ordinal)))
            return false;

        foreach ((string name, var allowedValues) in family.ParameterValues)
        {
            if (!row.Parameters.TryGetValue(name, out string? actual) || !allowedValues.Contains(actual, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static void ValidateFamilyParity(IReadOnlyList<BenchmarkCaseConfiguration> families, ISet<string> reasons)
    {
        if (families.Count != 2)
            return;
        BenchmarkCaseConfiguration left = families[0];
        BenchmarkCaseConfiguration right = families[1];
        if (left.Classification != right.Classification || left.Operation != right.Operation ||
            left.DatasetProfile != right.DatasetProfile || left.WorkloadId != right.WorkloadId ||
            left.ResultContract != right.ResultContract || left.CachePolicy != right.CachePolicy ||
            left.LifecyclePolicy != right.LifecyclePolicy || !SemanticParametersEqual(left.SemanticParameters, right.SemanticParameters) ||
            string.IsNullOrWhiteSpace(left.QualityProof) || string.IsNullOrWhiteSpace(right.QualityProof))
            reasons.Add("SemanticParityMismatch");
    }

    private static void ValidatePair(
        BenchmarkComparisonEvidence evidence,
        BenchmarkCaseConfiguration leftFamily,
        BenchmarkCaseConfiguration rightFamily,
        MatchedCase left,
        MatchedCase right,
        ISet<string> reasons)
    {
        if (!IsValidCommit(evidence.SourceCommit) || evidence.SourceDirty)
            reasons.Add("SourceTreeDirty");
        if (!evidence.Host.IsComplete)
            reasons.Add("HostIdentityIncomplete");

        ValidateReport(evidence, leftFamily, left.Report, reasons);
        ValidateReport(evidence, rightFamily, right.Report, reasons);
        if (!string.Equals(left.Report.DatasetFingerprintSha256, right.Report.DatasetFingerprintSha256, StringComparison.OrdinalIgnoreCase))
            reasons.Add("DatasetMismatch");

        ValidateCase(left.Row, reasons);
        ValidateCase(right.Row, reasons);
        if (!Same(left.Row.RuntimeName, right.Row.RuntimeName) ||
            !Same(left.Row.RuntimeVersion, right.Row.RuntimeVersion) ||
            !Same(left.Row.ToolchainType, right.Row.ToolchainType) ||
            !Same(left.Row.ToolchainRuntimeName, right.Row.ToolchainRuntimeName) ||
            !Same(left.Row.ToolchainRuntimeVersion, right.Row.ToolchainRuntimeVersion))
            reasons.Add("RuntimeMismatch");

        IReadOnlyDictionary<string, string> leftGc = SelectSettings(left.Row.JobSettings, "gc.");
        IReadOnlyDictionary<string, string> rightGc = SelectSettings(right.Row.JobSettings, "gc.");
        if (!HasValue(leftGc, "gc.server") || !HasValue(leftGc, "gc.concurrent") ||
            !HasValue(rightGc, "gc.server") || !HasValue(rightGc, "gc.concurrent"))
            reasons.Add("GcIdentityMissing");
        else if (!MapsEqual(leftGc, rightGc))
            reasons.Add("GcSettingsMismatch");

        string? leftPgo = GetIgnoreCase(left.Row.EnvironmentVariables, "DOTNET_TieredPGO");
        string? rightPgo = GetIgnoreCase(right.Row.EnvironmentVariables, "DOTNET_TieredPGO");
        if (string.IsNullOrWhiteSpace(leftPgo) || string.IsNullOrWhiteSpace(rightPgo))
            reasons.Add("PgoIdentityMissing");
        else if (!string.Equals(leftPgo, rightPgo, StringComparison.OrdinalIgnoreCase) ||
                 !MapsEqual(SelectSettings(left.Row.EnvironmentVariables, "DOTNET_"), SelectSettings(right.Row.EnvironmentVariables, "DOTNET_")))
            reasons.Add("PgoSettingsMismatch");
    }

    private static void ValidateReport(
        BenchmarkComparisonEvidence evidence,
        BenchmarkCaseConfiguration family,
        BenchmarkComparisonSourceReport report,
        ISet<string> reasons)
    {
        if (!string.IsNullOrWhiteSpace(report.ReadFailure))
            reasons.Add("CoreReportInvalid");
        if (report.SchemaVersion == 4)
            reasons.Add("HistoricalResultSchema");
        else if (report.SchemaVersion != CurrentSchemaVersion)
            reasons.Add("UnsupportedResultSchema");

        if (string.IsNullOrWhiteSpace(report.SourceCommit) || !string.Equals(report.SourceCommit, evidence.SourceCommit, StringComparison.OrdinalIgnoreCase))
            reasons.Add("SourceCommitMismatch");
        if (report.SourceDirty is null)
            reasons.Add("SourceDirtyStateMissing");
        else if (report.SourceDirty.Value)
            reasons.Add("SourceTreeDirty");

        if (!string.Equals(report.HostMachineName, evidence.Host.MachineName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(report.OsDescription, evidence.Host.OsDescription, StringComparison.Ordinal) ||
            !string.Equals(report.RuntimeIdentifier, evidence.Host.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(report.ProcessArchitecture, evidence.Host.ProcessArchitecture, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(report.RuntimeFramework, evidence.Host.RuntimeFramework, StringComparison.Ordinal))
            reasons.Add("HostIdentityMismatch");

        if (!BenchmarkRunIdentityValidator.IsSha256(report.DatasetFingerprintSha256))
            reasons.Add("DatasetIdentityMissing");
        if (!report.DatasetProfiles.Contains(family.DatasetProfile, StringComparer.Ordinal))
            reasons.Add("DatasetProfileMismatch");
        if (!string.Equals(report.BenchmarkDotNetVersion, RequiredBenchmarkDotNetVersion, StringComparison.Ordinal))
            reasons.Add("BenchmarkDotNetVersionMismatch");
    }

    private static void ValidateCase(BenchmarkComparisonCase row, ISet<string> reasons)
    {
        if (row.ResultState != "TerminalResult")
            reasons.Add("NonTerminalOrFailedResult");
        if (row.IdentityComplete != true)
            reasons.Add("IncompleteCaseIdentity");
        if (row.PreflightPassed is null)
            reasons.Add("PreflightEvidenceMissing");
        else if (!row.PreflightPassed.Value)
            reasons.Add("PreflightFailed");
        if (row.QualityFlags.Count != 0)
            reasons.Add("CaseQualityFlagsPresent");

        if (row.SampleCount is null || row.SampleCount < MinimumSamples ||
            row.ActualWorkloadMeasurementCount is null || row.ActualWorkloadMeasurementCount < MinimumSamples)
            reasons.Add("InsufficientSamples");
        if (row.CoefficientOfVariationPercent is not { } coefficient || !double.IsFinite(coefficient) || coefficient > MaximumCoefficientOfVariationPercent)
            reasons.Add("NoisyMeasurements");
        if (row.MeanNanoseconds is not { } mean || !double.IsFinite(mean) || mean <= 0)
            reasons.Add("InvalidStatistics");
    }

    private static BenchmarkComparisonPairAssessment BuildPair(
        string group,
        string parameterKey,
        IReadOnlyList<BenchmarkCaseConfiguration> families,
        MatchedCase? left,
        MatchedCase? right,
        SortedSet<string> reasons)
    {
        double? ratio = null;
        if (reasons.Count == 0 && left?.Row.MeanNanoseconds is { } leftMean && right?.Row.MeanNanoseconds is { } rightMean &&
            leftMean > 0 && rightMean > 0 && double.IsFinite(leftMean) && double.IsFinite(rightMean))
            ratio = leftMean / rightMean;
        bool eligible = ratio is not null;
        return new BenchmarkComparisonPairAssessment(
            group,
            parameterKey,
            families.ElementAtOrDefault(0)?.Id ?? string.Empty,
            families.ElementAtOrDefault(0)?.EngineId ?? string.Empty,
            families.ElementAtOrDefault(1)?.Id ?? string.Empty,
            families.ElementAtOrDefault(1)?.EngineId ?? string.Empty,
            eligible ? "Qualified" : "Unqualified",
            eligible,
            ratio,
            reasons.ToArray());
    }

    private static bool IsParity(string classification)
        => classification is "StrictParity" or "QualifiedParity";

    private static bool IsValidCommit(string? value)
        => value is { Length: 40 or 64 } && value.All(Uri.IsHexDigit);

    private static bool SemanticParametersEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    private static string CanonicalParameters(IReadOnlyDictionary<string, string> parameters)
        => JsonSerializer.Serialize(parameters.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray());

    private static bool Same(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) && string.Equals(left, right, StringComparison.Ordinal);

    private static Dictionary<string, string> SelectSettings(IReadOnlyDictionary<string, string> values, string prefix)
        => values.Where(pair => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static bool HasValue(IReadOnlyDictionary<string, string> values, string name)
        => values.TryGetValue(name, out string? value) && !string.IsNullOrWhiteSpace(value);

    private static string? GetIgnoreCase(IReadOnlyDictionary<string, string> values, string name)
        => values.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    private static bool MapsEqual(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out string? value) && value == pair.Value);

    private sealed record MatchedCase(BenchmarkComparisonCase Row, BenchmarkComparisonSourceReport Report, string ParameterKey);
}
