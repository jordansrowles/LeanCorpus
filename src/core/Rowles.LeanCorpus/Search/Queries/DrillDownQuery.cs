using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>One exact-value or hierarchical-path selection for a <see cref="DrillDownQuery"/>.</summary>
public sealed class DrillDownSelection : IEquatable<DrillDownSelection>
{
    /// <summary>Initialises an exact string facet selection.</summary>
    /// <param name="field">The queryable facet dimension field.</param>
    /// <param name="value">The exact indexed facet value. Empty values are supported.</param>
    public DrillDownSelection(string field, string value)
    {
        Field = Document.Fields.FieldNameValidator.Validate(field, nameof(field));
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Initialises a hierarchical facet-path selection.</summary>
    /// <param name="field">The field populated by <see cref="FacetPathIndexer"/>.</param>
    /// <param name="path">The exact path to select.</param>
    public DrillDownSelection(string field, FacetPath path)
    {
        Field = Document.Fields.FieldNameValidator.Validate(field, nameof(field));
        Path = path ?? throw new ArgumentNullException(nameof(path));
    }

    /// <summary>Gets the facet dimension field.</summary>
    public string Field { get; }

    /// <summary>Gets the exact value, or <see langword="null"/> for a path selection.</summary>
    public string? Value { get; }

    /// <summary>Gets the selected path, or <see langword="null"/> for an exact-value selection.</summary>
    public FacetPath? Path { get; }

    /// <summary>Gets whether this selection targets a hierarchical path.</summary>
    public bool IsPath => Path is not null;

    internal string IndexedValue => Path is null
        ? Value!
        : FacetPathEncoder.Encode(Path.Components, Path.Components.Count);

    /// <inheritdoc/>
    public bool Equals(DrillDownSelection? other)
        => other is not null
            && string.Equals(Field, other.Field, StringComparison.Ordinal)
            && string.Equals(IndexedValue, other.IndexedValue, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is DrillDownSelection other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Field, IndexedValue);
}

/// <summary>
/// Filters a base query by facet selections. Selections in different dimensions
/// are combined with AND; selections in the same dimension are combined with OR.
/// </summary>
/// <remarks>
/// Exact selections use normal <see cref="TermQuery"/> clauses. Path selections
/// use the reversible values emitted by <see cref="FacetPathIndexer"/> and are
/// therefore queryable through the same postings index as ordinary string fields.
/// </remarks>
public sealed class DrillDownQuery : Query
{
    private readonly DrillDownSelection[] _selections;
    private readonly IReadOnlyList<DrillDownSelection> _readOnlySelections;

    /// <summary>Initialises a drill-down query from a base query and selections.</summary>
    public DrillDownQuery(Query baseQuery, params DrillDownSelection[] selections)
        : this(baseQuery, (IEnumerable<DrillDownSelection>)selections)
    {
    }

    /// <summary>Initialises a drill-down query from a base query and selections.</summary>
    public DrillDownQuery(Query baseQuery, IEnumerable<DrillDownSelection> selections)
    {
        BaseQuery = baseQuery ?? throw new ArgumentNullException(nameof(baseQuery));
        ArgumentNullException.ThrowIfNull(selections);

        var unique = new Dictionary<(string Field, string Value), DrillDownSelection>();
        foreach (var selection in selections)
        {
            ArgumentNullException.ThrowIfNull(selection);
            unique.TryAdd((selection.Field, selection.IndexedValue), selection);
        }

        _selections = unique.Values
            .OrderBy(static selection => selection.Field, StringComparer.Ordinal)
            .ThenBy(static selection => selection.IndexedValue, StringComparer.Ordinal)
            .ToArray();
        _readOnlySelections = Array.AsReadOnly(_selections);
    }

    /// <summary>Gets the unfiltered base query.</summary>
    public Query BaseQuery { get; }

    /// <summary>Gets the distinct, canonicalised selections.</summary>
    public IReadOnlyList<DrillDownSelection> Selections => _readOnlySelections;

    /// <inheritdoc/>
    public override string Field => string.Empty;

    /// <inheritdoc/>
    public override Query Rewrite()
    {
        var root = new BooleanQuery.Builder()
            .Add(BaseQuery, Occur.Must);

        int start = 0;
        while (start < _selections.Length)
        {
            int end = start + 1;
            while (end < _selections.Length
                && string.Equals(_selections[end].Field, _selections[start].Field, StringComparison.Ordinal))
                end++;

            int selectionCount = end - start;
            if (selectionCount == 1)
            {
                root.Add(ToTermQuery(_selections[start]), Occur.Must);
            }
            else
            {
                var sameDimension = new BooleanQuery.Builder();
                for (int i = start; i < end; i++)
                    sameDimension.Add(ToTermQuery(_selections[i]), Occur.Should);
                sameDimension.SetMinimumNumberShouldMatch(1);
                root.Add(sameDimension.Build(), Occur.Must);
            }

            start = end;
        }

        var rewritten = root.Build();
        rewritten.Boost = Boost;
        return rewritten;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is DrillDownQuery other
            && Boost.Equals(other.Boost)
            && BaseQuery.Equals(other.BaseQuery)
            && _selections.SequenceEqual(other._selections);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(DrillDownQuery));
        hash.Add(BaseQuery);
        foreach (var selection in _selections)
            hash.Add(selection);
        return CombineBoost(hash.ToHashCode());
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        BaseQuery.Visit(visitor.GetSubVisitor(Occur.Must, this));

        int start = 0;
        while (start < _selections.Length)
        {
            int end = start + 1;
            while (end < _selections.Length
                && string.Equals(_selections[end].Field, _selections[start].Field, StringComparison.Ordinal))
                end++;

            Occur occur = end - start == 1 ? Occur.Must : Occur.Should;
            for (int i = start; i < end; i++)
                ToTermQuery(_selections[i]).Visit(visitor.GetSubVisitor(occur, this));
            start = end;
        }
    }

    private static TermQuery ToTermQuery(DrillDownSelection selection)
        => new(selection.Field, selection.IndexedValue);

}
