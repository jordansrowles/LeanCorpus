using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal static class BenchmarkCaseManifestReader
{
    private static readonly HashSet<string> EngineIds = new(StringComparer.Ordinal)
    {
        "leancorpus", "lucenenet", "reference-scalar", "reference-java-lucene", "system"
    };

    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "Index", "Search", "Analysis", "Storage", "Vector", "Spatial", "Lifecycle", "Diagnostic"
    };

    private static readonly HashSet<string> CachePolicies = new(StringComparer.Ordinal)
    {
        "Disabled", "UncachedExpansion", "WarmExpansion", "WarmResults", "Mixed", "NotApplicable"
    };

    private static readonly HashSet<string> LifecyclePolicies = new(StringComparer.Ordinal)
    {
        "existing-index", "new-index", "cold-open", "warm-open", "commit", "merge", "refresh", "NotApplicable"
    };

    private static readonly HashSet<string> Classifications = new(StringComparer.Ordinal)
    {
        "StrictParity", "QualifiedParity", "Directional", "InternalReference", "Unqualified"
    };

    private static readonly HashSet<string> MetricRoles = new(StringComparer.Ordinal)
    {
        "primary", "secondary"
    };

    public static BenchmarkCaseManifestConfiguration Read(JsonElement root, string path)
    {
        RejectDuplicateProperties(root, path, "$", caseInsensitive: true);
        RequireObjectProperties(root, path, "$", ["schemaVersion", "caseFamilies", "exclusions"], ["schemaVersion", "caseFamilies", "exclusions"]);
        if (ReadInt(root.GetProperty("schemaVersion"), path, "$.schemaVersion") != 1)
            throw Error(path, "$.schemaVersion", "unsupported version; supported version is 1");

        var caseFamilies = root.GetProperty("caseFamilies");
        if (caseFamilies.ValueKind != JsonValueKind.Array || caseFamilies.GetArrayLength() == 0)
            throw Error(path, "$.caseFamilies", "must contain at least one benchmark case family");

        var cases = ImmutableArray.CreateBuilder<BenchmarkCaseConfiguration>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in caseFamilies.EnumerateArray())
        {
            var propertyPath = $"$.caseFamilies[{index}]";
            var benchmarkCase = ReadCase(item, path, propertyPath);
            if (!ids.Add(benchmarkCase.Id))
                throw Error(path, propertyPath + ".id", $"duplicate benchmark case family ID '{benchmarkCase.Id}'");
            cases.Add(benchmarkCase);
            index++;
        }

        ValidateComparisonGroups(cases, path);

        var exclusionsElement = root.GetProperty("exclusions");
        if (exclusionsElement.ValueKind != JsonValueKind.Array)
            throw Error(path, "$.exclusions", "must be an array");

        var exclusions = ImmutableArray.CreateBuilder<BenchmarkCaseExclusionConfiguration>();
        var exclusionTargets = new HashSet<string>(StringComparer.Ordinal);
        index = 0;
        foreach (var item in exclusionsElement.EnumerateArray())
        {
            var propertyPath = $"$.exclusions[{index}]";
            var exclusion = ReadExclusion(item, path, propertyPath);
            var target = string.Join("\0", exclusion.Project, exclusion.BenchmarkClass, exclusion.BenchmarkMethod,
                string.Join(";", exclusion.ParameterValues.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => pair.Key + "=" + string.Join(",", pair.Value))));
            if (!exclusionTargets.Add(target))
                throw Error(path, propertyPath, "duplicates an existing benchmark exclusion target");
            exclusions.Add(exclusion);
            index++;
        }

        return new BenchmarkCaseManifestConfiguration(cases.ToImmutable(), exclusions.ToImmutable());
    }

    private static BenchmarkCaseExclusionConfiguration ReadExclusion(JsonElement value, string path, string propertyPath)
    {
        RequireObjectProperties(value, path, propertyPath,
            ["project", "benchmarkClass", "benchmarkMethod", "parameterValues", "reason", "expiresOn"],
            ["project", "benchmarkClass", "benchmarkMethod", "reason", "expiresOn"]);

        var project = ReadString(value.GetProperty("project"), path, propertyPath + ".project");
        if (!IsStableId(project))
            throw Error(path, propertyPath + ".project", $"'{project}' is not a lower-case stable project ID");
        var benchmarkClass = ReadString(value.GetProperty("benchmarkClass"), path, propertyPath + ".benchmarkClass");
        var benchmarkMethod = ReadString(value.GetProperty("benchmarkMethod"), path, propertyPath + ".benchmarkMethod");
        var parameterValues = value.TryGetProperty("parameterValues", out var parametersElement)
            ? ReadParameterValues(parametersElement, path, propertyPath + ".parameterValues")
            : ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal);
        var reason = ReadString(value.GetProperty("reason"), path, propertyPath + ".reason");
        var expiresText = ReadString(value.GetProperty("expiresOn"), path, propertyPath + ".expiresOn");
        if (!DateOnly.TryParseExact(expiresText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiresOn))
            throw Error(path, propertyPath + ".expiresOn", "must use the ISO date format yyyy-MM-dd");

        return new BenchmarkCaseExclusionConfiguration(project, benchmarkClass, benchmarkMethod, parameterValues, reason, expiresOn);
    }

    private static BenchmarkCaseConfiguration ReadCase(JsonElement value, string path, string propertyPath)
    {
        RequireObjectProperties(value, path, propertyPath,
            ["id", "project", "benchmarkClass", "benchmarkMethods", "methodAliases", "parameterValues", "semanticParameters", "engineId", "category", "operation", "datasetProfile", "workloadId", "algorithmFamily", "cachePolicy", "lifecyclePolicy", "comparisonGroup", "classification", "resultContract", "qualityProof", "metrics", "estimatedBudget"],
            ["id", "project", "benchmarkClass", "benchmarkMethods", "engineId", "category", "operation", "datasetProfile", "workloadId", "algorithmFamily", "cachePolicy", "lifecyclePolicy", "comparisonGroup", "classification", "resultContract", "qualityProof", "metrics", "estimatedBudget"]);

        var id = ReadString(value.GetProperty("id"), path, propertyPath + ".id");
        if (!IsStableId(id))
            throw Error(path, propertyPath + ".id", $"'{id}' is not a lower-case stable case family ID");

        var project = ReadString(value.GetProperty("project"), path, propertyPath + ".project");
        if (!IsStableId(project))
            throw Error(path, propertyPath + ".project", $"'{project}' is not a lower-case stable project ID");

        var benchmarkClass = ReadString(value.GetProperty("benchmarkClass"), path, propertyPath + ".benchmarkClass");
        var methods = ReadStringArray(value.GetProperty("benchmarkMethods"), path, propertyPath + ".benchmarkMethods", requireNonEmpty: true);
        var methodAliases = value.TryGetProperty("methodAliases", out var aliasesElement)
            ? ReadStringArray(aliasesElement, path, propertyPath + ".methodAliases", requireNonEmpty: false)
            : [];
        var parameterValues = value.TryGetProperty("parameterValues", out var parameterValuesElement)
            ? ReadParameterValues(parameterValuesElement, path, propertyPath + ".parameterValues")
            : ImmutableDictionary.Create<string, ImmutableArray<string>>(StringComparer.Ordinal);
        var semanticParameters = value.TryGetProperty("semanticParameters", out var semanticParametersElement)
            ? ReadSemanticParameters(semanticParametersElement, path, propertyPath + ".semanticParameters")
            : ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

        var engineId = ReadString(value.GetProperty("engineId"), path, propertyPath + ".engineId");
        if (!EngineIds.Contains(engineId))
            throw Error(path, propertyPath + ".engineId", $"unknown engine ID '{engineId}'");

        var category = ReadString(value.GetProperty("category"), path, propertyPath + ".category");
        if (!Categories.Contains(category))
            throw Error(path, propertyPath + ".category", $"unknown benchmark category '{category}'");

        var operation = ReadString(value.GetProperty("operation"), path, propertyPath + ".operation");
        var datasetProfile = ReadString(value.GetProperty("datasetProfile"), path, propertyPath + ".datasetProfile");
        var workloadId = ReadString(value.GetProperty("workloadId"), path, propertyPath + ".workloadId");
        if (!IsStableId(workloadId))
            throw Error(path, propertyPath + ".workloadId", $"'{workloadId}' is not a lower-case stable workload ID");

        var algorithmFamily = ReadString(value.GetProperty("algorithmFamily"), path, propertyPath + ".algorithmFamily");
        var cachePolicy = ReadString(value.GetProperty("cachePolicy"), path, propertyPath + ".cachePolicy");
        if (!CachePolicies.Contains(cachePolicy))
            throw Error(path, propertyPath + ".cachePolicy", $"unknown cache policy '{cachePolicy}'");

        var lifecyclePolicy = ReadString(value.GetProperty("lifecyclePolicy"), path, propertyPath + ".lifecyclePolicy");
        if (!LifecyclePolicies.Contains(lifecyclePolicy))
            throw Error(path, propertyPath + ".lifecyclePolicy", $"unknown lifecycle policy '{lifecyclePolicy}'");

        var comparisonGroupElement = value.GetProperty("comparisonGroup");
        var comparisonGroup = comparisonGroupElement.ValueKind == JsonValueKind.Null
            ? null
            : ReadString(comparisonGroupElement, path, propertyPath + ".comparisonGroup");
        if (comparisonGroup is not null && !IsStableId(comparisonGroup))
            throw Error(path, propertyPath + ".comparisonGroup", $"'{comparisonGroup}' is not a lower-case stable comparison group ID");

        var classification = ReadString(value.GetProperty("classification"), path, propertyPath + ".classification");
        if (!Classifications.Contains(classification))
            throw Error(path, propertyPath + ".classification", $"unknown comparison class '{classification}'");
        if (classification is "StrictParity" or "QualifiedParity" && comparisonGroup is null)
            throw Error(path, propertyPath + ".comparisonGroup", $"{classification} requires a comparisonGroup");

        var resultContract = ReadString(value.GetProperty("resultContract"), path, propertyPath + ".resultContract");
        var qualityProof = ReadString(value.GetProperty("qualityProof"), path, propertyPath + ".qualityProof");
        var metrics = ReadMetrics(value.GetProperty("metrics"), path, propertyPath + ".metrics");
        if (metrics.Count(static metric => metric.Role == "primary") != 1)
            throw Error(path, propertyPath + ".metrics", "must declare exactly one primary metric");

        var estimatedBudget = ReadString(value.GetProperty("estimatedBudget"), path, propertyPath + ".estimatedBudget");
        return new BenchmarkCaseConfiguration(id, project, benchmarkClass, methods, methodAliases, parameterValues, semanticParameters,
            engineId, category, operation, datasetProfile, workloadId, algorithmFamily, cachePolicy, lifecyclePolicy,
            comparisonGroup, classification, resultContract, qualityProof, metrics, estimatedBudget);
    }

    private static ImmutableArray<BenchmarkMetricConfiguration> ReadMetrics(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() == 0)
            throw Error(path, propertyPath, "must contain at least one metric");

        var metrics = ImmutableArray.CreateBuilder<BenchmarkMetricConfiguration>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemPath = $"{propertyPath}[{index}]";
            RequireObjectProperties(item, path, itemPath, ["name", "unit", "role"], ["name", "unit", "role"]);
            var name = ReadString(item.GetProperty("name"), path, itemPath + ".name");
            if (!names.Add(name))
                throw Error(path, itemPath + ".name", $"duplicate metric name '{name}'");
            var unit = ReadString(item.GetProperty("unit"), path, itemPath + ".unit");
            var role = ReadString(item.GetProperty("role"), path, itemPath + ".role");
            if (!MetricRoles.Contains(role))
                throw Error(path, itemPath + ".role", $"unknown metric role '{role}'");
            metrics.Add(new BenchmarkMetricConfiguration(name, unit, role));
            index++;
        }

        return metrics.ToImmutable();
    }

    private static ImmutableDictionary<string, ImmutableArray<string>> ReadParameterValues(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Error(path, propertyPath, "must be an object keyed by stable BenchmarkDotNet parameter name");

        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(StringComparer.Ordinal);
        foreach (var parameter in value.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                throw Error(path, propertyPath, "parameter names cannot be empty");
            var values = ReadStringArray(parameter.Value, path, propertyPath + "." + parameter.Name, requireNonEmpty: true);
            builder.Add(parameter.Name, values);
        }
        return builder.ToImmutable();
    }

    private static ImmutableDictionary<string, string> ReadSemanticParameters(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Error(path, propertyPath, "must be an object keyed by semantic parameter name");

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var parameter in value.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(parameter.Name))
                throw Error(path, propertyPath, "semantic parameter names cannot be empty");
            builder.Add(parameter.Name, ReadString(parameter.Value, path, propertyPath + "." + parameter.Name));
        }
        return builder.ToImmutable();
    }

    private static void ValidateComparisonGroups(ImmutableArray<BenchmarkCaseConfiguration>.Builder cases, string path)
    {
        foreach (var group in cases
                     .Where(static benchmarkCase => benchmarkCase.ComparisonGroup is not null)
                     .GroupBy(static benchmarkCase => benchmarkCase.ComparisonGroup!, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            var expectedParameters = members[0].SemanticParameters;
            if (members.Skip(1).Any(member => !SemanticParametersEqual(expectedParameters, member.SemanticParameters)))
                throw Error(path, "$.caseFamilies",
                    $"comparison group '{group.Key}' has incompatible semantic parameters");
        }

        var parityCases = cases.Where(static benchmarkCase => benchmarkCase.Classification is "StrictParity" or "QualifiedParity");
        foreach (var group in parityCases.GroupBy(static benchmarkCase => benchmarkCase.ComparisonGroup!, StringComparer.Ordinal))
        {
            var members = group.ToArray();
            if (members.Select(static item => item.EngineId).Distinct(StringComparer.Ordinal).Count() < 2)
                throw Error(path, "$.caseFamilies", $"parity comparison group '{group.Key}' must contain at least two distinct engines");
            if (members.Select(static item => item.Classification).Distinct(StringComparer.Ordinal).Count() != 1 ||
                members.Select(static item => item.WorkloadId).Distinct(StringComparer.Ordinal).Count() != 1 ||
                members.Select(static item => item.DatasetProfile).Distinct(StringComparer.Ordinal).Count() != 1 ||
                members.Select(static item => item.Operation).Distinct(StringComparer.Ordinal).Count() != 1)
                throw Error(path, "$.caseFamilies", $"parity comparison group '{group.Key}' has incompatible classification or workload metadata");
        }
    }

    private static bool SemanticParametersEqual(
        ImmutableDictionary<string, string> left,
        ImmutableDictionary<string, string> right)
        => left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static ImmutableArray<string> ReadStringArray(JsonElement value, string path, string propertyPath, bool requireNonEmpty)
    {
        if (value.ValueKind != JsonValueKind.Array)
            throw Error(path, propertyPath, "must be an array of non-empty strings");
        var values = ImmutableArray.CreateBuilder<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            var itemValue = ReadString(item, path, $"{propertyPath}[{index}]");
            if (!seen.Add(itemValue))
                throw Error(path, $"{propertyPath}[{index}]", $"duplicate value '{itemValue}'");
            values.Add(itemValue);
            index++;
        }
        if (requireNonEmpty && values.Count == 0)
            throw Error(path, propertyPath, "must contain at least one value");
        return values.ToImmutable();
    }

    private static string ReadString(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw Error(path, propertyPath, "must be a non-empty string");
        return value.GetString()!;
    }

    private static int ReadInt(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
            throw Error(path, propertyPath, "must be an integer");
        return result;
    }

    private static void RequireObjectProperties(JsonElement value, string path, string propertyPath,
        IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw Error(path, propertyPath, "must be an object");
        var allowedNames = new HashSet<string>(allowed, StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowedNames.Contains(property.Name))
                throw Error(path, propertyPath, $"contains unknown property '{property.Name}'");
        }
        foreach (var property in required)
        {
            if (!value.TryGetProperty(property, out _))
                throw Error(path, propertyPath + "." + property, $"required property '{property}' is missing");
        }
    }

    private static void RejectDuplicateProperties(JsonElement value, string path, string propertyPath, bool caseInsensitive)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var seen = new HashSet<string>(comparer);
            foreach (var property in value.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                    throw Error(path, propertyPath, $"contains duplicate property '{property.Name}'");
                RejectDuplicateProperties(property.Value, path, propertyPath + "." + property.Name, caseInsensitive);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                RejectDuplicateProperties(item, path, $"{propertyPath}[{index++}]", caseInsensitive);
        }
    }

    private static bool IsStableId(string value)
    {
        if (value.Length == 0 || value[0] is '-' or '.' || value[^1] is '-' or '.')
            return false;
        var previousSeparator = false;
        foreach (var character in value)
        {
            if (character is '-' or '.')
            {
                if (previousSeparator)
                    return false;
                previousSeparator = true;
                continue;
            }
            if (!((character >= 'a' && character <= 'z') || (character >= '0' && character <= '9')))
                return false;
            previousSeparator = false;
        }
        return true;
    }

    private static InvalidDataException Error(string path, string propertyPath, string message)
        => new($"Configuration '{path}' {propertyPath}: {message}.");
}
