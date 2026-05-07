namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// Defines the sort order applied to documents within a segment at flush time.
/// When configured, documents are physically reordered before writing.
/// </summary>
public sealed class IndexSort : IEquatable<IndexSort>
{
    /// <summary>Gets the sort fields that define the document order within a segment.</summary>
    public IReadOnlyList<SortField> Fields { get; }

    /// <summary>
    /// Initialises a new <see cref="IndexSort"/> with the specified sort fields.
    /// </summary>
    /// <param name="fields">One or more sort fields that define the document ordering. Score sort type is not allowed.</param>
    /// <exception cref="ArgumentException">Thrown if no fields are provided, or if any field uses <see cref="SortFieldType.Score"/>.</exception>
    public IndexSort(params SortField[] fields)
    {
        if (fields.Length == 0)
            throw new ArgumentException("At least one sort field is required.", nameof(fields));
        foreach (var f in fields)
        {
            if (f.Type == SortFieldType.Score)
                throw new ArgumentException("Index sort cannot use Score sort type.", nameof(fields));
        }
        Fields = fields.ToArray();
    }

    /// <inheritdoc/>
    public bool Equals(IndexSort? other)
    {
        if (other is null || Fields.Count != other.Fields.Count) return false;
        for (int i = 0; i < Fields.Count; i++)
        {
            var a = Fields[i];
            var b = other.Fields[i];
            if (a.Type != b.Type || a.FieldName != b.FieldName || a.Descending != b.Descending)
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
        }
        return hc.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
        => string.Join(", ", Fields.Select(f => $"{f.FieldName}:{f.Type}{(f.Descending ? " DESC" : "")}"));
}
