namespace Rowles.LeanLucene.Search;

/// <summary>
/// Boolean clause occurrence type.
/// </summary>
public enum Occur
{
    /// <summary>Document must match this clause. Non-matching documents are excluded.</summary>
    Must,

    /// <summary>Document may match this clause; contributes to relevance scoring but is not required.</summary>
    Should,

    /// <summary>Document must not match this clause; any matching document is excluded from results.</summary>
    MustNot
}
