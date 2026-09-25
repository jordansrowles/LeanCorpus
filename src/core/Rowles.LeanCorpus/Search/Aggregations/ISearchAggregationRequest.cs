namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Identifies an aggregation that can run alongside a search.</summary>
public interface ISearchAggregationRequest
{
    /// <summary>Gets the caller-assigned aggregation name.</summary>
    string Name { get; }

    /// <summary>Gets the field to aggregate.</summary>
    string Field { get; }
}

