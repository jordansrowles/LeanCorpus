using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

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

    /// <summary>Gets the immutable origin for a geographic distance sort.</summary>
    public GeoPoint? GeoOrigin { get; }

    /// <summary>Gets the immutable origin for a Cartesian distance sort.</summary>
    public XYPoint? XYOrigin { get; }

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
        if (type is SortFieldType.GeoDistance or SortFieldType.XYDistance)
            throw new ArgumentException("Use the typed distance-sort factory to supply its origin.", nameof(type));

        Type = type;
        FieldName = fieldName;
        Descending = descending;
        Selector = selector;
        GeoOrigin = null;
        XYOrigin = null;
    }

    private SortField(
        SortFieldType type,
        string fieldName,
        bool descending,
        GeoPoint? geoOrigin,
        XYPoint? xyOrigin)
    {
        Type = type;
        FieldName = fieldName;
        Descending = descending;
        Selector = SortValueSelector.Min;
        GeoOrigin = geoOrigin;
        XYOrigin = xyOrigin;
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

    /// <summary>Creates a geographic distance sort measured in metres from <paramref name="origin"/>.</summary>
    public static SortField GeoDistance(string fieldName, GeoPoint origin, bool descending = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return new SortField(SortFieldType.GeoDistance, fieldName, descending, origin, null);
    }

    /// <summary>Creates a Cartesian distance sort measured in coordinate units from <paramref name="origin"/>.</summary>
    public static SortField XYDistance(string fieldName, XYPoint origin, bool descending = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        return new SortField(SortFieldType.XYDistance, fieldName, descending, null, origin);
    }

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
