namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Defines the sort order applied to documents within a segment at flush time.
/// When configured, documents are physically reordered before writing.
/// </summary>
public sealed class IndexSort : IEquatable<IndexSort>
{
    /// <summary>Gets the sort fields that define the document order within a segment.</summary>
    public IReadOnlyList<SortField> Fields { get; }

    /// <summary>
    /// Gets a pre-computed serialised representation of the sort fields used for
    /// segment metadata persistence. Each entry encodes <c>Type:FieldName:Descending</c>.
    /// </summary>
    internal List<string> SerialisedFields { get; }

    /// <summary>
    /// Initialises a new <see cref="IndexSort"/> with the specified sort fields.
    /// </summary>
    /// <param name="fields">One or more sort fields that define the document ordering. Score and point-distance sort types are not allowed.</param>
    /// <exception cref="ArgumentException">Thrown if no fields are provided, or if any field uses a score or point-distance sort type.</exception>
    public IndexSort(params SortField[] fields)
    {
        if (fields.Length == 0)
            throw new ArgumentException("At least one sort field is required.", nameof(fields));
        foreach (var f in fields)
        {
            if (f.Type is SortFieldType.Score or SortFieldType.GeoDistance or SortFieldType.XYDistance)
                throw new ArgumentException("Index sort cannot use score or point-distance sort types.", nameof(fields));
        }
        Fields = fields.ToArray();
        var serialised = new List<string>(fields.Length);
        foreach (var f in fields)
        {
            // Keep the original three-part form for the default selector so
            // existing segment metadata remains readable. Persist a non-default
            // selector because it changes the physical order and early-termination
            // must be able to reconstruct the exact sort.
            serialised.Add(f.Selector == SortValueSelector.Min
                ? $"{f.Type}:{f.FieldName}:{f.Descending}"
                : $"{f.Type}:{f.FieldName}:{f.Descending}:{f.Selector}");
        }
        SerialisedFields = serialised;
    }

    /// <inheritdoc/>
    public bool Equals(IndexSort? other)
    {
        if (other is null || Fields.Count != other.Fields.Count) return false;
        for (int i = 0; i < Fields.Count; i++)
        {
            var a = Fields[i];
            var b = other.Fields[i];
            if (a.Type != b.Type || a.FieldName != b.FieldName
                || a.Descending != b.Descending || a.Selector != b.Selector
                || a.GeoOrigin != b.GeoOrigin || a.XYOrigin != b.XYOrigin)
                return false;
        }
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as IndexSort);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hc = new HashCode();
        foreach (var f in Fields)
        {
            hc.Add(f.Type);
            hc.Add(f.FieldName);
            hc.Add(f.Descending);
            hc.Add(f.Selector);
            hc.Add(f.GeoOrigin);
            hc.Add(f.XYOrigin);
        }
        return hc.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
        => string.Join(", ", Fields.Select(f => $"{f.FieldName}:{f.Type}{(f.Descending ? " DESC" : "")}"));
}
