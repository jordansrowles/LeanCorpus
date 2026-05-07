namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>Specifies the data type and sort behaviour for a field-based sort criterion.</summary>
public enum SortFieldType
{
    /// <summary>Sort by BM25 relevance score (descending by default).</summary>
    Score,
    /// <summary>Sort by internal document ID.</summary>
    DocId,
    /// <summary>Sort by a numeric stored/indexed field.</summary>
    Numeric,
    /// <summary>Sort by a string stored field (lexicographic).</summary>
    String
}
