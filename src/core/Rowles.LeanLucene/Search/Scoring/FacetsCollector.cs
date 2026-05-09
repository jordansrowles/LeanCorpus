namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Collects per-field value counts across a result set for faceted navigation.
/// </summary>
public sealed class FacetsCollector
{
    private readonly Dictionary<string, Dictionary<string, int>> _fieldValueCounts = new(StringComparer.Ordinal);

    /// <summary>Records a facet value hit for a document.</summary>
    internal void Collect(string field, string value)
    {
        if (!_fieldValueCounts.TryGetValue(field, out var counts))
        {
            counts = new Dictionary<string, int>(StringComparer.Ordinal);
            _fieldValueCounts[field] = counts;
        }

        counts.TryGetValue(value, out int current);
        counts[value] = current + 1;
    }

    /// <summary>Returns the accumulated facet results, sorted by count descending.</summary>
    public IReadOnlyList<FacetResult> GetResults()
    {
        var results = new List<FacetResult>(_fieldValueCounts.Count);
        foreach (var (field, counts) in _fieldValueCounts)
        {
            // Manual loop avoids LINQ allocation overhead
            var buckets = new List<FacetBucket>(counts.Count);
            foreach (var kvp in counts)
                buckets.Add(new FacetBucket(kvp.Key, kvp.Value));
            buckets.Sort((a, b) => b.Count.CompareTo(a.Count));
            results.Add(new FacetResult(field, buckets));
        }
        return results;
    }
}
