using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record BenchmarkHostIdentity(
    string MachineName,
    string OsDescription,
    string RuntimeIdentifier,
    string ProcessArchitecture,
    int LogicalProcessorCount,
    string CpuIdentifier,
    string RuntimeFramework,
    bool ServerGc,
    string FingerprintSha256)
{
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(MachineName) &&
        !string.IsNullOrWhiteSpace(OsDescription) &&
        !string.IsNullOrWhiteSpace(RuntimeIdentifier) &&
        !string.IsNullOrWhiteSpace(ProcessArchitecture) &&
        LogicalProcessorCount > 0 &&
        !string.IsNullOrWhiteSpace(CpuIdentifier) &&
        !string.IsNullOrWhiteSpace(RuntimeFramework) &&
        BenchmarkRunIdentityValidator.IsSha256(FingerprintSha256);

    public static BenchmarkHostIdentity Capture()
    {
        var identity = new BenchmarkHostIdentity(
            Environment.MachineName,
            RuntimeInformation.OSDescription.Trim(),
            RuntimeInformation.RuntimeIdentifier,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            ReadCpuIdentifier() ?? string.Empty,
            RuntimeInformation.FrameworkDescription,
            GCSettings.IsServerGC,
            string.Empty);
        return identity with { FingerprintSha256 = CreateFingerprint(identity) };
    }

    public IReadOnlyDictionary<string, string> ToRunInputs()
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hostMachineName"] = MachineName,
            ["hostOsDescription"] = OsDescription,
            ["hostRuntimeIdentifier"] = RuntimeIdentifier,
            ["hostProcessArchitecture"] = ProcessArchitecture,
            ["hostLogicalProcessorCount"] = LogicalProcessorCount.ToString(CultureInfo.InvariantCulture),
            ["hostCpuIdentifier"] = CpuIdentifier,
            ["hostRuntimeFramework"] = RuntimeFramework,
            ["hostServerGc"] = ServerGc.ToString().ToLowerInvariant(),
            ["hostFingerprintSha256"] = FingerprintSha256
        };

    private static string CreateFingerprint(BenchmarkHostIdentity identity)
    {
        var content = JsonSerializer.SerializeToUtf8Bytes(new
        {
            machineName = identity.MachineName,
            osDescription = identity.OsDescription,
            runtimeIdentifier = identity.RuntimeIdentifier,
            processArchitecture = identity.ProcessArchitecture,
            logicalProcessorCount = identity.LogicalProcessorCount,
            cpuIdentifier = identity.CpuIdentifier,
            runtimeFramework = identity.RuntimeFramework,
            serverGc = identity.ServerGc
        });
        return Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
    }

    private static string? ReadCpuIdentifier()
    {
        if (OperatingSystem.IsLinux())
        {
            try
            {
                foreach (var line in File.ReadLines("/proc/cpuinfo"))
                {
                    var separator = line.IndexOf(':');
                    if (separator <= 0)
                        continue;
                    var name = line[..separator].Trim();
                    if (name is "model name" or "Hardware" or "Processor")
                    {
                        var value = line[(separator + 1)..].Trim();
                        if (value.Length > 0)
                            return value;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var environmentIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(environmentIdentifier))
            return environmentIdentifier.Trim();

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "/usr/sbin/sysctl",
                    ArgumentList = { "-n", "machdep.cpu.brand_string" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                if (process is null)
                    return null;

                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(2_000))
                {
                    process.Kill(entireProcessTree: true);
                    return null;
                }
                return output.GetAwaiter().GetResult().Trim() is { Length: > 0 } value ? value : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }
        }

        return null;
    }
}

internal sealed record BenchmarkCoreReportIdentity(
    string Project,
    string ReportPath,
    int? SchemaVersion,
    DateTimeOffset? GeneratedAtUtc,
    string? SourceCommit,
    bool? SourceDirty,
    string? HostMachineName,
    string? OsDescription,
    string? RuntimeIdentifier,
    string? ProcessArchitecture,
    string? RuntimeFramework,
    string? DatasetFingerprintSha256,
    bool HasDatasetIdentity,
    IReadOnlyList<JsonElement> Datasets,
    string? BenchmarkDotNetVersion,
    string? ReadFailure)
{
    public bool ResultRowsValid { get; init; } = true;
    public string? ResultRowsFailure { get; init; }

    public static BenchmarkCoreReportIdentity Read(string project, string reportPath)
    {
        if (!File.Exists(reportPath))
            return Missing(project, reportPath, "Core benchmark report is missing.");

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(reportPath));
            var root = document.RootElement;
            var provenance = TryGetObject(root, "provenance");
            var datasets = ReadDatasets(provenance);
            var datasetFingerprint = GetString(provenance, "dataFingerprintSha256");
            var datasetIdentityComplete = datasets.Count > 0 && datasets.All(IsCompleteDatasetIdentity);
            int? schemaVersion = GetInt32(root, "schemaVersion");
            (bool resultRowsValid, string? resultRowsFailure) = schemaVersion == 5
                ? ValidateV5ResultRows(root)
                : (true, null);
            return new BenchmarkCoreReportIdentity(
                project,
                reportPath,
                schemaVersion,
                GetDateTimeOffset(root, "generatedAtUtc"),
                GetString(provenance, "sourceCommit"),
                GetBoolean(provenance, "gitDirty"),
                GetString(root, "hostMachineName"),
                GetString(provenance, "osDescription"),
                GetString(provenance, "runtimeIdentifier"),
                GetString(provenance, "processArchitecture"),
                GetString(provenance, "runtimeFramework"),
                datasetFingerprint,
                datasetIdentityComplete,
                datasets,
                GetString(provenance, "benchmarkDotNetVersion"),
                null)
            {
                ResultRowsValid = resultRowsValid,
                ResultRowsFailure = resultRowsFailure
            };
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return Missing(project, reportPath, exception.Message);
        }
    }

    private static BenchmarkCoreReportIdentity Missing(string project, string reportPath, string failure)
        => new(project, reportPath, null, null, null, null, null, null, null, null, null,
            null, false, [], null, failure);

    private static (bool Valid, string? Failure) ValidateV5ResultRows(JsonElement root)
    {
        if (!root.TryGetProperty("suites", out var suites) || suites.ValueKind != JsonValueKind.Array || suites.GetArrayLength() == 0)
            return (false, "Schema v5 report has no benchmark suites.");

        var resultRowIds = new HashSet<string>(StringComparer.Ordinal);
        int suiteRegisteredTotal = 0;
        int suiteRowTotal = 0;
        foreach (JsonElement suite in suites.EnumerateArray())
        {
            if (suite.ValueKind != JsonValueKind.Object ||
                !suite.TryGetProperty("benchmarks", out var benchmarks) || benchmarks.ValueKind != JsonValueKind.Array)
                return (false, "A schema v5 suite has no benchmark row array.");

            int registeredCount = GetInt32(suite, "registeredBenchmarkCount") ?? -1;
            int rowCount = GetInt32(suite, "benchmarkCount") ?? -1;
            int measuredCount = GetInt32(suite, "measuredBenchmarkCount") ?? -1;
            int failedCount = GetInt32(suite, "failedBenchmarkCount") ?? -1;
            int intermediateCount = GetInt32(suite, "intermediateBenchmarkCount") ?? -1;
            int missingCount = GetInt32(suite, "missingBenchmarkCount") ?? -1;
            if (registeredCount <= 0 || rowCount != benchmarks.GetArrayLength())
                return (false, "Schema v5 suite counts do not match the row array.");

            var terminalCaseIdentities = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement row in benchmarks.EnumerateArray())
            {
                string? state = GetString(row, "resultState");
                string? caseIdentity = GetString(row, "caseIdentitySha256");
                string? resultRowId = GetString(row, "resultRowId");
                int measurementCount = GetInt32(row, "measurementCount") ?? -1;
                int actualWorkloadMeasurementCount = GetInt32(row, "actualWorkloadMeasurementCount") ?? -1;
                if (state != "TerminalResult" ||
                    !BenchmarkRunIdentityValidator.IsSha256(caseIdentity) ||
                    !BenchmarkRunIdentityValidator.IsSha256(resultRowId) ||
                    !resultRowIds.Add(resultRowId!) ||
                    GetBoolean(row, "identityComplete") != true ||
                    string.IsNullOrWhiteSpace(GetString(row, "key")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "jobId")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "resolvedJobId")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "jobDisplayInfo")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "runtimeName")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "runtimeVersion")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "toolchainType")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "toolchainDisplayInfo")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "toolchainRuntimeName")) ||
                    string.IsNullOrWhiteSpace(GetString(row, "toolchainRuntimeVersion")) ||
                    !BenchmarkRunIdentityValidator.IsSha256(GetString(row, "measurementFingerprintSha256")) ||
                    measurementCount <= 0 || actualWorkloadMeasurementCount <= 0 || actualWorkloadMeasurementCount > measurementCount ||
                    !HasObject(row, "parameters") || !HasObject(row, "jobSettings") || !HasObject(row, "environmentVariables") ||
                    !HasValidTerminalStatistics(row))
                    return (false, "Schema v5 report contains a missing, duplicate, non-terminal or incomplete result row.");

                terminalCaseIdentities.Add(caseIdentity!);
            }

            if (terminalCaseIdentities.Count != measuredCount ||
                failedCount != 0 || intermediateCount != 0 || missingCount != 0 || measuredCount != registeredCount)
                return (false, "Schema v5 suite counts include failed, incomplete or unmeasured cases.");

            suiteRegisteredTotal += registeredCount;
            suiteRowTotal += rowCount;
        }

        if (GetInt32(root, "registeredBenchmarkCount") != suiteRegisteredTotal ||
            GetInt32(root, "totalBenchmarkCount") != suiteRowTotal)
            return (false, "Schema v5 run counts do not match its suites.");

        return (true, null);
    }

    private static bool HasValidTerminalStatistics(JsonElement row)
    {
        JsonElement? statistics = TryGetObject(row, "statistics");
        if (statistics is not { ValueKind: JsonValueKind.Object } value ||
            GetInt32(value, "sampleCount") is not > 0 ||
            GetFiniteDouble(value, "meanNanoseconds") is not { } mean || mean <= 0 ||
            GetFiniteDouble(value, "medianNanoseconds") is not { } median ||
            GetFiniteDouble(value, "minNanoseconds") is not { } min ||
            GetFiniteDouble(value, "maxNanoseconds") is not { } max ||
            GetFiniteDouble(value, "standardDeviationNanoseconds") is not { } standardDeviation || standardDeviation < 0 ||
            GetFiniteDouble(value, "coefficientOfVariationPercent") is not { } coefficientOfVariation || coefficientOfVariation < 0 ||
            GetFiniteDouble(value, "operationsPerSecond") is not { } operationsPerSecond || operationsPerSecond <= 0)
            return false;

        return min <= median && median <= max && min <= mean && mean <= max;
    }

    private static bool HasObject(JsonElement parent, string property)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object;

    private static double? GetFiniteDouble(JsonElement parent, string property)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number)
            ? number
            : null;

    private static IReadOnlyList<JsonElement> ReadDatasets(JsonElement? provenance)
    {
        if (provenance is not { ValueKind: JsonValueKind.Object } value ||
            !value.TryGetProperty("datasets", out var datasets) || datasets.ValueKind != JsonValueKind.Array)
            return [];
        return datasets.EnumerateArray().Select(static dataset => dataset.Clone()).ToArray();
    }

    private static bool IsCompleteDatasetIdentity(JsonElement dataset)
    {
        if (dataset.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(GetString(dataset, "sourceKind")) ||
            !BenchmarkRunIdentityValidator.IsSha256(GetString(dataset, "contentSha256")) ||
            !GetInt32(dataset, "recordCount").HasValue)
            return false;

        var profileId = GetString(dataset, "profileId");
        var profileVersion = GetInt32(dataset, "profileVersion");
        var datasetId = GetString(dataset, "datasetId");
        var datasetVersion = GetInt32(dataset, "datasetVersion");
        return (!string.IsNullOrWhiteSpace(profileId) && profileVersion.HasValue) ||
            (!string.IsNullOrWhiteSpace(datasetId) && datasetVersion.HasValue);
    }

    private static JsonElement? TryGetObject(JsonElement parent, string property)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? GetString(JsonElement? parent, string property)
        => parent is { ValueKind: JsonValueKind.Object } value ? GetString(value, property) : null;

    private static string? GetString(JsonElement parent, string property)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement parent, string property)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool? GetBoolean(JsonElement? parent, string property)
        => parent is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty(property, out var item) &&
           item.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? item.GetBoolean()
            : null;

    private static DateTimeOffset? GetDateTimeOffset(JsonElement parent, string property)
        => parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
               DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}

internal sealed record BenchmarkRunIdentityInput(
    string? SourceCommit,
    bool? SourceDirty,
    BenchmarkHostIdentity Host,
    IReadOnlyList<BenchmarkCoreReportIdentity> CoreReports,
    int ExpectedCoreReportCount);

internal sealed record BenchmarkRunIdentityAssessment(
    string IdentityStatus,
    string QualificationState,
    bool EligibleForRatio,
    IReadOnlyList<string> ReasonCodes)
{
    public bool Comparable => QualificationState == "Qualified" && EligibleForRatio;
}

internal sealed record BenchmarkRunIdentityEvidence(
    BenchmarkHostIdentity Host,
    IReadOnlyList<BenchmarkCoreReportIdentity> CoreReports,
    BenchmarkRunIdentityAssessment Assessment,
    BenchmarkComparisonAssessment ComparisonAssessment,
    BenchmarkExecutionSummary Execution);

internal sealed record BenchmarkExecutionSummary(
    string Classification,
    int PlannedInvocationCount,
    int TerminalInvocationCount,
    int PartialInvocationCount,
    int PlannedCaseCount,
    int TerminalCaseCount,
    int FailedCaseCount,
    int IntermediateCaseCount,
    int MissingCaseCount,
    int RawResultFileCount);

internal static class BenchmarkRunIdentityValidator
{
    private const int QualifiableCoreReportSchemaVersion = 5;
    private const string RequiredBenchmarkDotNetVersion = "0.16.0-preview.2";

    public static BenchmarkRunIdentityAssessment Assess(BenchmarkRunIdentityInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Host);
        ArgumentNullException.ThrowIfNull(input.CoreReports);
        var reasons = new SortedSet<string>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(input.SourceCommit))
            reasons.Add("SourceCommitMissing");
        else if (!IsGitCommit(input.SourceCommit))
            reasons.Add("SourceCommitInvalid");

        if (input.SourceDirty is null)
            reasons.Add("SourceDirtyStateMissing");
        else if (input.SourceDirty.Value)
            reasons.Add("SourceTreeDirty");

        if (!input.Host.IsComplete)
            reasons.Add("HostIdentityIncomplete");

        if (input.ExpectedCoreReportCount <= 0 || input.CoreReports.Count == 0)
        {
            reasons.Add("CoreReportIdentityMissing");
            reasons.Add("DatasetIdentityMissing");
        }
        if (input.CoreReports.Count < input.ExpectedCoreReportCount)
            reasons.Add("CoreReportIdentityMissing");

        foreach (var report in input.CoreReports)
        {
            if (!string.IsNullOrWhiteSpace(report.ReadFailure))
                reasons.Add("CoreReportInvalid");

            if (string.IsNullOrWhiteSpace(report.SourceCommit))
                reasons.Add("ChildSourceIdentityMissing");
            else if (!IsGitCommit(report.SourceCommit))
                reasons.Add("ChildSourceIdentityInvalid");
            else if (!string.Equals(input.SourceCommit, report.SourceCommit, StringComparison.OrdinalIgnoreCase))
                reasons.Add("ChildSourceMismatch");

            if (report.SourceDirty is null)
                reasons.Add("ChildDirtyStateMissing");
            else if (report.SourceDirty.Value)
                reasons.Add("ChildSourceDirty");

            ValidateHost(report, input.Host, reasons);
            ValidateDataset(report, reasons);

            if (report.SchemaVersion is null)
                reasons.Add("CoreReportIdentityMissing");
            else if (report.SchemaVersion == 4)
                reasons.Add("HistoricalResultSchema");
            else if (report.SchemaVersion != QualifiableCoreReportSchemaVersion)
                reasons.Add("UnsupportedResultSchema");
            else if (!report.ResultRowsValid)
                reasons.Add("BenchmarkRowsInvalid");

            if (report.GeneratedAtUtc is { Year: 2026, Month: 8 })
                reasons.Add("HistoricalAugust2026Result");

            if (string.IsNullOrWhiteSpace(report.BenchmarkDotNetVersion))
                reasons.Add("BenchmarkDotNetVersionMissing");
            else if (!string.Equals(NormaliseVersion(report.BenchmarkDotNetVersion), RequiredBenchmarkDotNetVersion, StringComparison.Ordinal))
                reasons.Add("BenchmarkDotNetVersionMismatch");
        }

        var identityStatus = reasons.Count == 0 ? "Validated" : "Rejected";
        reasons.Add("ComparisonEvidencePending");
        return new BenchmarkRunIdentityAssessment(
            identityStatus,
            "Unqualified",
            EligibleForRatio: false,
            reasons.ToArray());
    }

    internal static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void ValidateHost(BenchmarkCoreReportIdentity report, BenchmarkHostIdentity host, ISet<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(report.HostMachineName) ||
            string.IsNullOrWhiteSpace(report.OsDescription) ||
            string.IsNullOrWhiteSpace(report.RuntimeIdentifier) ||
            string.IsNullOrWhiteSpace(report.ProcessArchitecture))
        {
            reasons.Add("ChildHostIdentityMissing");
            return;
        }

        if (!string.Equals(host.MachineName, report.HostMachineName, StringComparison.OrdinalIgnoreCase))
            reasons.Add("HostMachineMismatch");
        if (!string.Equals(host.OsDescription, report.OsDescription, StringComparison.Ordinal))
            reasons.Add("HostOsMismatch");
        if (!string.Equals(host.RuntimeIdentifier, report.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase))
            reasons.Add("HostRuntimeIdentifierMismatch");
        if (!string.Equals(host.ProcessArchitecture, report.ProcessArchitecture, StringComparison.OrdinalIgnoreCase))
            reasons.Add("HostArchitectureMismatch");
    }

    private static void ValidateDataset(BenchmarkCoreReportIdentity report, ISet<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(report.DatasetFingerprintSha256))
            reasons.Add("DatasetFingerprintMissing");
        else if (!IsSha256(report.DatasetFingerprintSha256))
            reasons.Add("DatasetFingerprintInvalid");

        if (!report.HasDatasetIdentity)
            reasons.Add("DatasetIdentityMissing");
    }

    private static bool IsGitCommit(string value)
        => value.Length is 40 or 64 && value.All(Uri.IsHexDigit);

    private static string NormaliseVersion(string value)
        => value.Split('+', 2)[0].Trim();
}
