using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Running;

namespace Rowles.LeanCorpus.Benchmarks.Infrastructure;

internal static class BenchmarkParameterSelectionFilter
{
    internal const string EnvironmentVariable = "LEANCORPUS_BENCHMARK_PARAMETER_FILTER";

    public static IConfig Apply(IConfig configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string? json = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(json))
            return configuration;

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Environment variable '{EnvironmentVariable}' must contain a JSON object.");

        var parameters = ImmutableDictionary.CreateBuilder<string, ImmutableHashSet<string>>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException($"Environment variable '{EnvironmentVariable}' contains an invalid parameter entry.");

            var values = new List<string>();
            foreach (var value in property.Value.EnumerateArray())
            {
                string? parameterValue = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                if (string.IsNullOrWhiteSpace(parameterValue))
                    throw new InvalidDataException($"Environment variable '{EnvironmentVariable}' parameter '{property.Name}' must have non-empty string values.");
                values.Add(parameterValue);
            }
            if (values.Count == 0)
                throw new InvalidDataException($"Environment variable '{EnvironmentVariable}' parameter '{property.Name}' must have non-empty string values.");
            if (!parameters.TryAdd(property.Name, values.ToImmutableHashSet(StringComparer.Ordinal)))
                throw new InvalidDataException($"Environment variable '{EnvironmentVariable}' contains duplicate parameter '{property.Name}'.");
        }

        if (parameters.Count == 0)
            throw new InvalidDataException($"Environment variable '{EnvironmentVariable}' must select at least one parameter.");

        return configuration.AddFilter(new ExactParameterFilter(parameters.ToImmutable()));
    }

    private sealed class ExactParameterFilter(
        ImmutableDictionary<string, ImmutableHashSet<string>> expectedParameters) : IFilter
    {
        public bool Predicate(BenchmarkCase benchmarkCase)
        {
            ArgumentNullException.ThrowIfNull(benchmarkCase);

            var actualParameters = benchmarkCase.Parameters.Items.ToDictionary(
                static parameter => parameter.Name,
                static parameter => Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal);
            if (actualParameters.Count != expectedParameters.Count)
                return false;

            return expectedParameters.All(pair => actualParameters.TryGetValue(pair.Key, out var value) && pair.Value.Contains(value));
        }
    }
}
