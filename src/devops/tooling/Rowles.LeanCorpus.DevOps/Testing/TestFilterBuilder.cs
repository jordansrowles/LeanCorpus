namespace Rowles.LeanCorpus.DevOps.Testing;

internal static class TestFilterBuilder
{
    public static string Build(
        IEnumerable<string>? affectedAreas = null,
        IEnumerable<string>? areas = null,
        IEnumerable<string>? categories = null,
        IEnumerable<string>? techniques = null,
        string? filter = null,
        string? defaultFilter = null)
    {
        var parts = new List<string>();
        AddGroup(parts, "Area", affectedAreas);
        AddGroup(parts, "Area", areas);
        AddGroup(parts, "Category", categories);
        AddGroup(parts, "Technique", techniques);
        if (!string.IsNullOrWhiteSpace(filter))
        {
            parts.Add(filter.Trim());
        }

        var combined = string.Join('&', parts);
        if (string.IsNullOrWhiteSpace(defaultFilter))
        {
            return combined;
        }

        return combined.Length == 0 ? defaultFilter.Trim() : $"({combined})&({defaultFilter.Trim()})";
    }

    private static void AddGroup(List<string> parts, string traitName, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        var normalised = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (!normalised.TryGetValue(trimmed, out var existing) || StringComparer.Ordinal.Compare(trimmed, existing) < 0)
            {
                normalised[trimmed] = trimmed;
            }
        }

        var ordered = normalised.Values
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value, StringComparer.Ordinal)
            .Select(value => $"{traitName}={value}")
            .ToArray();
        if (ordered.Length == 1)
        {
            parts.Add($"({ordered[0]})");
        }
        else if (ordered.Length > 1)
        {
            parts.Add($"({string.Join('|', ordered)})");
        }
    }
}
