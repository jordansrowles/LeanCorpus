namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Specifies a field and direction for sorting search results.
/// </summary>
public sealed class SortField
{
    /// <summary>Sort by relevance score (default).</summary>
    public static readonly SortField Score = new(SortFieldType.Score, string.Empty);

    /// <summary>Sort by internal document ID (insertion order).</summary>
    public static readonly SortField DocId = new(SortFieldType.DocId, string.Empty);

    /// <summary>Gets the sort criterion type.</summary>
    public SortFieldType Type { get; }

    /// <summary>Gets the name of the field to sort by. Empty for <see cref="SortFieldType.Score"/> and <see cref="SortFieldType.DocId"/>.</summary>
    public string FieldName { get; }

    /// <summary>Gets a value indicating whether results are sorted in descending order.</summary>
    public bool Descending { get; }

    /// <summary>Initialises a new <see cref="SortField"/> with the given type, field name, and direction.</summary>
    /// <param name="type">The kind of value to sort by.</param>
    /// <param name="fieldName">The field name for <see cref="SortFieldType.Numeric"/> and <see cref="SortFieldType.String"/> sorts.</param>
    /// <param name="descending">When <see langword="true"/>, results are ordered largest-first.</param>
    public SortField(SortFieldType type, string fieldName, bool descending = false)
    {
        Type = type;
        FieldName = fieldName;
        Descending = descending;
    }

    /// <summary>Creates a numeric sort on the given field.</summary>
    public static SortField Numeric(string fieldName, bool descending = false)
        => new(SortFieldType.Numeric, fieldName, descending);

    /// <summary>Creates a string sort on the given field.</summary>
    public static SortField String(string fieldName, bool descending = false)
        => new(SortFieldType.String, fieldName, descending);
}
