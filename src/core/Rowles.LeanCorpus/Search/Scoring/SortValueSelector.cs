namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Selects a value from a multi-valued sort field.</summary>
public enum SortValueSelector
{
    /// <summary>Use the lowest value.</summary>
    Min,

    /// <summary>Use the highest value.</summary>
    Max
}
