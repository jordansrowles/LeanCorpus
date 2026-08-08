namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Specifies a field and direction for sorting search results.
/// </summary>
public sealed class SortField
{
    /// <summary>Sort by relevance score (default).</summary>
    public static readonly SortField Score = new(SortFieldType.Score, string.Empty, descending: true);

    /// <summary>Sort by internal document ID (insertion order).</summary>
    public static readonly SortField DocId = new(SortFieldType.DocId, string.Empty);

    /// <summary>Gets the sort criterion type.</summary>
    public SortFieldType Type { get; }

    /// <summary>Gets the name of the field to sort by. Empty for <see cref="SortFieldType.Score"/> and <see cref="SortFieldType.DocId"/>.</summary>
    public string FieldName { get; }

    /// <summary>Gets a value indicating whether results are sorted in descending order.</summary>
    public bool Descending { get; }

    /// <summary>Gets the value selector used for multi-valued fields.</summary>
    public SortValueSelector Selector { get; }

    /// <summary>Initialises a new <see cref="SortField"/> with the given type, field name, and direction.</summary>
    /// <param name="type">The kind of value to sort by.</param>
    /// <param name="fieldName">The field name for <see cref="SortFieldType.Numeric"/> and <see cref="SortFieldType.String"/> sorts.</param>
    /// <param name="descending">When <see langword="true"/>, results are ordered largest-first.</param>
    /// <param name="selector">The value selected from a multi-valued field.</param>
    public SortField(
        SortFieldType type,
        string fieldName,
        bool descending = false,
        SortValueSelector selector = SortValueSelector.Min)
    {
        Type = type;
        FieldName = fieldName;
        Descending = descending;
        Selector = selector;
    }

    /// <summary>Creates a numeric sort on the given field.</summary>
    public static SortField Numeric(string fieldName, bool descending = false)
        => new(SortFieldType.Numeric, fieldName, descending);

    /// <summary>Creates a 64-bit integer sort on the given field.</summary>
    public static SortField Int64(string fieldName, bool descending = false)
        => new(SortFieldType.Int64, fieldName, descending);

    /// <summary>Creates a string sort on the given field.</summary>
    public static SortField String(string fieldName, bool descending = false)
        => new(SortFieldType.String, fieldName, descending);

    /// <summary>Creates a multi-valued numeric sort on the given field.</summary>
    public static SortField SortedNumeric(
        string fieldName,
        SortValueSelector selector = SortValueSelector.Min,
        bool descending = false)
        => new(SortFieldType.Numeric, fieldName, descending, selector);

    /// <summary>Creates a multi-valued 64-bit integer sort on the given field.</summary>
    public static SortField SortedInt64(
        string fieldName,
        SortValueSelector selector = SortValueSelector.Min,
        bool descending = false)
        => new(SortFieldType.Int64, fieldName, descending, selector);
}
