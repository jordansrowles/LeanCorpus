namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Identifies the result of an aggregation that ran alongside a search.</summary>
public interface ISearchAggregationResult
{
    /// <summary>Gets the caller-assigned aggregation name.</summary>
    string Name { get; }

    /// <summary>Gets the field that was aggregated.</summary>
    string Field { get; }
}
