using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed record DataForgeWikipediaIdentity(
    string SourceKind,
    string DatasetId,
    int DatasetVersion,
    int RecordCount,
    long LogicalByteCount,
    string ContentSha256,
    string ArtefactSha256,
    string DumpDate,
    string PrimarySha1,
    string IndexSha1);

internal sealed record DataForgeSyntheticIdentity(
    string ProfileId,
    int ProfileVersion,
    JsonElement Seed,
    int RecordCount,
    JsonElement Parameters,
    string ContentSha256,
    List<string> BenchmarkRunIds);

internal sealed record DataForgeSyntheticIdentityReadResult(
    IReadOnlyList<DataForgeSyntheticIdentity> Identities,
    IReadOnlyList<string> Failures);

internal sealed record DataForgeAvailableProfile(string ProfileId, int ProfileVersion);

internal sealed record DataForgeProfileCatalogReadResult(
    IReadOnlyList<DataForgeAvailableProfile> Profiles,
    string? Failure);

internal sealed record DataForgeMtpTestResult(bool Passed, int? TotalTests, string? Failure);

internal static class DataForgeQualificationEvidence
{
    public static DataForgeProfileCatalogReadResult ReadProfileCatalog(string runDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        var outputPath = Path.Combine(runDirectory, "process.stdout.log");
        if (!File.Exists(outputPath))
            return new DataForgeProfileCatalogReadResult([], $"DataForge profile listing output is missing: '{outputPath}'.");

        try
        {
            foreach (var line in File.ReadLines(outputPath).Reverse())
            {
                JsonDocument? document = null;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                using (document)
                {
                    if (document.RootElement.ValueKind != JsonValueKind.Array)
                        continue;

                    var profiles = new List<DataForgeAvailableProfile>();
                    var ids = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in document.RootElement.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object ||
                            !item.TryGetProperty("profileId", out var idElement) || idElement.ValueKind != JsonValueKind.String ||
                            !item.TryGetProperty("profileVersion", out var versionElement) ||
                            !versionElement.TryGetInt32(out var version) || version < 1)
                            throw new InvalidDataException("DataForge profile listing contains an invalid profile descriptor.");

                        var profileId = idElement.GetString();
                        if (string.IsNullOrWhiteSpace(profileId) || !ids.Add(profileId))
                            throw new InvalidDataException($"DataForge profile listing contains an empty or duplicate profile ID '{profileId}'.");
                        profiles.Add(new DataForgeAvailableProfile(profileId, version));
                    }

                    if (profiles.Count == 0)
                        throw new InvalidDataException("DataForge profile listing contains no profile descriptors.");
                    return new DataForgeProfileCatalogReadResult(profiles.OrderBy(static item => item.ProfileId, StringComparer.Ordinal).ToArray(), null);
                }
            }

            return new DataForgeProfileCatalogReadResult([], "DataForge profile listing did not contain a JSON descriptor array.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new DataForgeProfileCatalogReadResult([], exception.Message);
        }
    }

    public static DataForgeMtpTestResult ReadMtpTestTrx(string trxPath)
    {
        if (!File.Exists(trxPath))
            return new DataForgeMtpTestResult(false, null, $"MTP TRX result file is missing: '{trxPath}'.");

        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(trxPath, settings);
            var document = XDocument.Load(reader, LoadOptions.None);
            var summary = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "ResultSummary");
            var counters = summary?.Elements().FirstOrDefault(element => element.Name.LocalName == "Counters");
            if (summary is null || counters is null)
                return new DataForgeMtpTestResult(false, null, "MTP TRX is missing its ResultSummary counters.");

            var total = ReadCounter(counters, "total");
            var executed = ReadCounter(counters, "executed");
            var failed = ReadCounter(counters, "failed");
            var errors = ReadCounter(counters, "error");
            var timeouts = ReadCounter(counters, "timeout");
            var aborted = ReadCounter(counters, "aborted");
            var inconclusive = ReadCounter(counters, "inconclusive");
            var notExecuted = ReadCounter(counters, "notExecuted");
            var disconnected = ReadCounter(counters, "disconnected");
            if (new[] { total, executed, failed, errors, timeouts, aborted, inconclusive, notExecuted, disconnected }.Any(value => value is null))
                return new DataForgeMtpTestResult(false, total, "MTP TRX counters are incomplete.");

            var failures = (long)failed!.Value + errors!.Value + timeouts!.Value + aborted!.Value +
                inconclusive!.Value + notExecuted!.Value + disconnected!.Value;
            if (!string.Equals((string?)summary.Attribute("outcome"), "Completed", StringComparison.OrdinalIgnoreCase))
                return new DataForgeMtpTestResult(false, total, "MTP TRX result is not Completed.");
            if (total < 1 || executed < 1)
                return new DataForgeMtpTestResult(false, total, "MTP TRX contains no executed tests.");
            if (failures != 0)
                return new DataForgeMtpTestResult(false, total, $"MTP TRX records {failures} failed, errored, timed out, aborted, inconclusive, unexecuted or disconnected tests.");

            return new DataForgeMtpTestResult(true, total, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or XmlException or InvalidOperationException or FormatException)
        {
            return new DataForgeMtpTestResult(false, null, $"MTP TRX could not be read or validated: {exception.Message}");
        }
    }

    public static DataForgeWikipediaIdentity? ReadReferenceIdentity(string referencePath, out string? failure)
    {
        failure = null;
        var manifestPath = Path.Combine(referencePath, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            failure = $"The frozen Wikipedia v1 reference is missing at '{referencePath}'; qualification does not download or rebuild it.";
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = document.RootElement;
            var source = root.GetProperty("source");
            var datasetVersionElement = source.GetProperty("datasetVersion");
            var datasetVersion = datasetVersionElement.ValueKind switch
            {
                JsonValueKind.Number => datasetVersionElement.GetInt32(),
                JsonValueKind.String => int.Parse(datasetVersionElement.GetString()!, System.Globalization.CultureInfo.InvariantCulture),
                _ => throw new InvalidDataException("The frozen Wikipedia manifest datasetVersion is not an integer value.")
            };
            var identity = new DataForgeWikipediaIdentity(
                root.GetProperty("sourceKind").GetString() ?? string.Empty,
                source.GetProperty("datasetId").GetString() ?? string.Empty,
                datasetVersion,
                root.GetProperty("recordCount").GetInt32(),
                root.GetProperty("logicalByteCount").GetInt64(),
                root.GetProperty("contentSha256").GetString() ?? string.Empty,
                root.GetProperty("artefactSha256").GetString() ?? string.Empty,
                source.GetProperty("dumpDate").GetString() ?? string.Empty,
                source.GetProperty("primarySha1").GetString() ?? string.Empty,
                source.GetProperty("indexSha1").GetString() ?? string.Empty);
            if (identity.SourceKind != "Imported" || identity.DatasetId != "leancorpus-wikipedia-en" ||
                identity.DatasetVersion != 1 || identity.RecordCount != 20_000 || identity.DumpDate != "20260901" ||
                !IsSha256(identity.ContentSha256) || !IsSha256(identity.ArtefactSha256))
            {
                failure = "The verified artefact manifest does not identify the required 20,000-record leancorpus-wikipedia-en v1 reference.";
                return null;
            }

            return identity;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            failure = $"Wikipedia reference identity validation failed: {exception.Message}";
            return null;
        }
    }

    public static string? ReadWikipediaReportError(string runDirectory, DataForgeWikipediaIdentity referenceIdentity)
    {
        var coreDirectory = Path.Combine(runDirectory, "core");
        string[] reportPaths;
        try
        {
            reportPaths = Directory.Exists(coreDirectory)
                ? Directory.EnumerateFiles(coreDirectory, "report.json", SearchOption.AllDirectories)
                    .OrderBy(static path => path, StringComparer.Ordinal)
                    .ToArray()
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"The normal benchmark reports could not be enumerated under '{coreDirectory}': {exception.Message}";
        }

        if (reportPaths.Length == 0)
            return $"The normal benchmark report is missing under '{coreDirectory}'.";

        foreach (var reportPath in reportPaths)
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(reportPath));
                var root = document.RootElement;
                if (root.GetProperty("schemaVersion").GetInt32() != 5)
                    return $"The benchmark report '{reportPath}' uses schema {root.GetProperty("schemaVersion")}; schema v5 is required.";
                var datasets = root.GetProperty("provenance").GetProperty("datasets");
                if (datasets.ValueKind != JsonValueKind.Array || datasets.GetArrayLength() != 1)
                    return $"The benchmark report '{reportPath}' contains {(datasets.ValueKind == JsonValueKind.Array ? datasets.GetArrayLength() : 0)} dataset identities; exactly one imported Wikipedia identity is required.";

                var identity = datasets[0];
                if (GetString(identity, "sourceKind") != "Imported" ||
                    GetInt(identity, "dataForgeVersion") != 1 ||
                    GetString(identity, "datasetId") != referenceIdentity.DatasetId ||
                    GetInt(identity, "datasetVersion") != referenceIdentity.DatasetVersion ||
                    GetInt(identity, "recordCount") != referenceIdentity.RecordCount ||
                    !string.Equals(GetString(identity, "contentSha256"), referenceIdentity.ContentSha256, StringComparison.OrdinalIgnoreCase))
                {
                    return $"The benchmark report dataset identity in '{reportPath}' does not match the verified Wikipedia v1 reference manifest.";
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                return $"The benchmark report '{reportPath}' could not be read or validated: {exception.Message}";
            }
        }

        return null;
    }

    public static DataForgeSyntheticIdentityReadResult ReadSyntheticIdentities(string runDirectory, string runId)
    {
        var identities = new Dictionary<string, DataForgeSyntheticIdentity>(StringComparer.Ordinal);
        var failures = new List<string>();
        if (!Directory.Exists(runDirectory))
            return new DataForgeSyntheticIdentityReadResult([], [$"Benchmark run directory '{runDirectory}' is missing."]);

        try
        {
            foreach (var path in Directory.EnumerateFiles(runDirectory, "*.json", SearchOption.AllDirectories)
                         .Where(path => string.Equals(new DirectoryInfo(Path.GetDirectoryName(path)!).Name, "dataforge", StringComparison.Ordinal)))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                    var root = document.RootElement;
                    var sourceKind = GetString(root, "sourceKind");
                    var dataForgeVersion = GetInt(root, "dataForgeVersion");
                    var recordCount = GetInt(root, "recordCount");
                    var contentSha = GetString(root, "contentSha256");
                    if (sourceKind != "Generated" || dataForgeVersion != 1 || recordCount < 1 || !IsSha256(contentSha))
                        throw new InvalidDataException($"Synthetic DataForge identity '{path}' is invalid.");

                    var profileId = GetString(root, "profileId");
                    var profileVersion = GetInt(root, "profileVersion");
                    if (string.IsNullOrWhiteSpace(profileId) || profileVersion < 1 ||
                        !root.TryGetProperty("parameters", out var parameters) ||
                        !root.TryGetProperty("seed", out var seed))
                    {
                        throw new InvalidDataException($"Synthetic DataForge identity '{path}' is missing profile, seed or parameters.");
                    }

                    var identity = new DataForgeSyntheticIdentity(profileId, profileVersion, seed.Clone(), recordCount,
                        parameters.Clone(), contentSha, [runId]);
                    var key = string.Join('|', profileId, profileVersion, recordCount, parameters.GetRawText(), contentSha);
                    if (identities.TryGetValue(key, out var prior))
                    {
                        if (!prior.BenchmarkRunIds.Contains(runId, StringComparer.Ordinal))
                            prior.BenchmarkRunIds.Add(runId);
                    }
                    else
                    {
                        identities.Add(key, identity);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or InvalidOperationException or KeyNotFoundException or FormatException)
                {
                    failures.Add(exception.Message);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failures.Add($"Could not enumerate DataForge benchmark identities: {exception.Message}");
        }

        return new DataForgeSyntheticIdentityReadResult(identities.Values.ToArray(), failures);
    }

    private static string GetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static int GetInt(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;

    private static bool IsSha256(string value)
        => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static int? ReadCounter(XElement counters, string name)
        => int.TryParse((string?)counters.Attribute(name), System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : null;
}
