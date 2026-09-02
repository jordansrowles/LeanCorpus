namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Requests immediate-child buckets from a hierarchical facet dimension.</summary>
public sealed class HierarchicalFacetRequest : IFacetRequest
{
    /// <summary>
    /// Initialises a hierarchical facet request.
    /// </summary>
    /// <param name="field">The facet dimension field.</param>
    /// <param name="parentPath">The parent path, or <see langword="null"/> for the root.</param>
    /// <param name="offset">The number of ordered buckets to skip.</param>
    /// <param name="limit">The maximum number of buckets to return.</param>
    /// <param name="order">The bucket ordering.</param>
    /// <param name="includeMissing">Whether matching documents without a hierarchy value should be counted.</param>
    /// <param name="name">Optional logical result name. Defaults to <paramref name="field"/>.</param>
    public HierarchicalFacetRequest(
        string field,
        FacetPath? parentPath = null,
        int offset = 0,
        int limit = int.MaxValue,
        FacetBucketOrder order = FacetBucketOrder.CountDescending,
        bool includeMissing = false,
        string? name = null)
    {
        Field = Document.Fields.FieldNameValidator.Validate(field, nameof(field));
        Name = ValidateName(name ?? Field);
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (limit < 0)
            throw new ArgumentOutOfRangeException(nameof(limit));
        if (!Enum.IsDefined(order))
            throw new ArgumentOutOfRangeException(nameof(order));

        ParentPath = parentPath;
        Offset = offset;
        Limit = limit;
        Order = order;
        IncludeMissing = includeMissing;
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Facet names must not be empty or whitespace.", nameof(name));
        return name;
    }

    /// <inheritdoc/>
    public string Field { get; }

    /// <inheritdoc/>
    public string Name { get; }

    /// <summary>Gets the parent path, or <see langword="null"/> for the root level.</summary>
    public FacetPath? ParentPath { get; }

    /// <inheritdoc/>
    public int Offset { get; }

    /// <inheritdoc/>
    public int Limit { get; }

    /// <inheritdoc/>
    public FacetBucketOrder Order { get; }

    /// <inheritdoc/>
    public bool IncludeMissing { get; }
}
