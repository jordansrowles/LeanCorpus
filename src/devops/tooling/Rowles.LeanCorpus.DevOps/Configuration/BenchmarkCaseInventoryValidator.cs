using System.Collections.Immutable;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal static class BenchmarkCaseInventoryValidator
{
    public static void Validate(ImmutableArray<BenchmarkCaseConfiguration> caseFamilies, string projectKey, string json)
        => Validate(caseFamilies, [], projectKey, json);

    public static void Validate(
        ImmutableArray<BenchmarkCaseConfiguration> caseFamilies,
        ImmutableArray<BenchmarkCaseExclusionConfiguration> exclusions,
        string projectKey,
        string json,
        DateOnly? utcToday = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw Error($"BDN case inventory for project '{projectKey}' is invalid JSON: {exception.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                throw Error($"BDN case inventory for project '{projectKey}' must be a non-empty JSON array.");

            var discoveredCases = ReadCases(document.RootElement, projectKey);
            var projectFamilies = caseFamilies.Where(item => item.Project.Equals(projectKey, StringComparison.Ordinal)).ToArray();
            var projectExclusions = exclusions.Where(item => item.Project.Equals(projectKey, StringComparison.Ordinal)).ToArray();
            var today = utcToday ?? DateOnly.FromDateTime(DateTime.UtcNow);

            foreach (var benchmarkCase in projectFamilies)
                ValidateFamily(benchmarkCase, discoveredCases);

            foreach (var exclusion in projectExclusions)
            {
                if (exclusion.ExpiresOn < today)
                    throw Error($"Benchmark exclusion for '{exclusion.BenchmarkClass}.{exclusion.BenchmarkMethod}' expired on {exclusion.ExpiresOn:yyyy-MM-dd}.");

                if (!discoveredCases.Any(discovered => MatchesExclusion(exclusion, discovered)))
                    throw Error($"Benchmark exclusion for '{exclusion.BenchmarkClass}.{exclusion.BenchmarkMethod}' matched no discovered BDN cases.");
            }

            foreach (var discovered in discoveredCases)
            {
                var familyMatches = projectFamilies.Count(benchmarkCase => MatchesFamily(benchmarkCase, discovered));
                var exclusionMatches = projectExclusions.Count(exclusion => MatchesExclusion(exclusion, discovered));
                if (familyMatches + exclusionMatches == 0)
                    throw Error($"Discovered BDN case '{discovered.FullName}' has no semantic case family or active exclusion.");
                if (familyMatches + exclusionMatches > 1)
                    throw Error($"Discovered BDN case '{discovered.FullName}' is covered by multiple semantic case families or exclusions.");
            }
        }
    }

    private static ImmutableArray<DiscoveredBenchmarkCase> ReadCases(JsonElement root, string projectKey)
    {
        var cases = ImmutableArray.CreateBuilder<DiscoveredBenchmarkCase>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in root.EnumerateArray())
        {
            var itemPath = $"$.discoveredCases[{index}]";
            if (item.ValueKind != JsonValueKind.Object)
                throw Error($"BDN case inventory for project '{projectKey}' {itemPath} must be an object.");

            var identity = ReadRequiredString(item, "uid", projectKey, itemPath);
            if (!identities.Add(identity))
                throw Error($"BDN case inventory for project '{projectKey}' contains duplicate BDN case identity '{identity}'.");
            var filterName = ReadRequiredString(item, "filterName", projectKey, itemPath);
            var fullName = ReadRequiredString(item, "fullName", projectKey, itemPath);
            if (!item.TryGetProperty("job", out var job) || job.ValueKind != JsonValueKind.Object)
                throw Error($"BDN case inventory for project '{projectKey}' {itemPath}.job must be an object.");
            _ = ReadRequiredString(job, "id", projectKey, itemPath + ".job");

            var separator = filterName.LastIndexOf('.');
            if (separator <= 0 || separator == filterName.Length - 1)
                throw Error($"BDN case inventory for project '{projectKey}' contains malformed benchmark name '{filterName}'.");
            var typeName = filterName[..separator];
            var typeSeparator = typeName.LastIndexOf('.');
            var benchmarkClass = typeName[(typeSeparator + 1)..];
            var benchmarkMethod = filterName[(separator + 1)..];
            cases.Add(new DiscoveredBenchmarkCase(benchmarkClass, benchmarkMethod, fullName, ReadParameterValues(fullName)));
            index++;
        }

        return cases.ToImmutable();
    }

    private static void ValidateFamily(BenchmarkCaseConfiguration benchmarkCase, ImmutableArray<DiscoveredBenchmarkCase> discoveredCases)
    {
        var methods = benchmarkCase.BenchmarkMethods
            .Concat(benchmarkCase.MethodAliases)
            .ToHashSet(StringComparer.Ordinal);
        var methodCases = discoveredCases
            .Where(discovered => discovered.BenchmarkClass.Equals(benchmarkCase.BenchmarkClass, StringComparison.Ordinal) &&
                methods.Contains(discovered.BenchmarkMethod))
            .ToArray();

        if (methodCases.Length == 0)
            throw Error($"Semantic case family '{benchmarkCase.Id}' maps to no BDN case in class '{benchmarkCase.BenchmarkClass}'.");

        foreach (var (parameterName, values) in benchmarkCase.ParameterValues)
        {
            foreach (var value in values)
            {
                if (!methodCases.Any(discovered => discovered.ParameterValues.TryGetValue(parameterName, out var discoveredValue) &&
                    discoveredValue.Equals(value, StringComparison.Ordinal)))
                    throw Error($"Semantic case family '{benchmarkCase.Id}' parameter '{parameterName}' value '{value}' was not discovered by BDN.");
            }
        }

        if (!methodCases.Any(discovered => MatchesFamily(benchmarkCase, discovered)))
            throw Error($"Semantic case family '{benchmarkCase.Id}' maps to no BDN case with its declared parameter fingerprint.");

        foreach (var discovered in methodCases.Where(discovered => MatchesFamily(benchmarkCase, discovered)))
        {
            if (discovered.ParameterValues.Count != benchmarkCase.ParameterValues.Count ||
                discovered.ParameterValues.Keys.Any(parameterName => !benchmarkCase.ParameterValues.ContainsKey(parameterName)))
            {
                throw Error(
                    $"Semantic case family '{benchmarkCase.Id}' must declare every discovered BDN parameter for '{discovered.FullName}' so bounded selection can count exact cases.");
            }
        }
    }

    private static bool MatchesFamily(BenchmarkCaseConfiguration benchmarkCase, DiscoveredBenchmarkCase discovered)
        => discovered.BenchmarkClass.Equals(benchmarkCase.BenchmarkClass, StringComparison.Ordinal) &&
            benchmarkCase.BenchmarkMethods.Concat(benchmarkCase.MethodAliases).Contains(discovered.BenchmarkMethod, StringComparer.Ordinal) &&
            MatchesParameters(benchmarkCase.ParameterValues, discovered.ParameterValues);

    private static bool MatchesExclusion(BenchmarkCaseExclusionConfiguration exclusion, DiscoveredBenchmarkCase discovered)
        => discovered.BenchmarkClass.Equals(exclusion.BenchmarkClass, StringComparison.Ordinal) &&
            discovered.BenchmarkMethod.Equals(exclusion.BenchmarkMethod, StringComparison.Ordinal) &&
            MatchesParameters(exclusion.ParameterValues, discovered.ParameterValues);

    private static bool MatchesParameters(
        ImmutableDictionary<string, ImmutableArray<string>> expectedValues,
        ImmutableDictionary<string, string> discoveredValues)
        => expectedValues.All(pair => discoveredValues.TryGetValue(pair.Key, out var discoveredValue) &&
            pair.Value.Contains(discoveredValue, StringComparer.Ordinal));

    private static ImmutableDictionary<string, string> ReadParameterValues(string fullName)
    {
        var opening = fullName.IndexOf('(');
        var closing = fullName.LastIndexOf(')');
        if (opening < 0 || closing <= opening)
            return ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

        var parameterText = fullName[(opening + 1)..closing];
        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var segment in SplitParameterSegments(parameterText))
        {
            var separator = segment.IndexOf(':');
            if (separator <= 0 || separator == segment.Length - 1)
                continue;
            var name = segment[..separator].Trim();
            var rawValue = segment[(separator + 1)..].Trim();
            var value = ReadParameterValue(rawValue);
            if (!builder.TryAdd(name, value))
                throw Error($"BDN case name '{fullName}' contains duplicate parameter '{name}'.");
        }
        return builder.ToImmutable();
    }

    private static IEnumerable<string> SplitParameterSegments(string parameterText)
    {
        var start = 0;
        var quoted = false;
        var escaped = false;
        for (var index = 0; index < parameterText.Length; index++)
        {
            var character = parameterText[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }
            if (quoted && character == '\\')
            {
                escaped = true;
                continue;
            }
            if (character == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (character == ',' && !quoted)
            {
                yield return parameterText[start..index].Trim();
                start = index + 1;
            }
        }
        if (start < parameterText.Length)
            yield return parameterText[start..].Trim();
    }

    private static string ReadParameterValue(string value)
    {
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            try
            {
                return JsonDocument.Parse(value).RootElement.GetString() ?? string.Empty;
            }
            catch (JsonException)
            {
                return value[1..^1];
            }
        }
        return value;
    }

    private static string ReadRequiredString(JsonElement value, string propertyName, string projectKey, string propertyPath)
    {
        if (!value.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
            throw Error($"BDN case inventory for project '{projectKey}' {propertyPath}.{propertyName} must be a non-empty string.");
        return element.GetString()!;
    }

    private static InvalidDataException Error(string message) => new(message);

    private sealed record DiscoveredBenchmarkCase(
        string BenchmarkClass,
        string BenchmarkMethod,
        string FullName,
        ImmutableDictionary<string, string> ParameterValues);
}
