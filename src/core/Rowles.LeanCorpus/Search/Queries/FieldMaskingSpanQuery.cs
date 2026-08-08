namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Reports spans from one field as though they belonged to another field.</summary>
public sealed class FieldMaskingSpanQuery : SpanQuery
{
    /// <summary>Gets the wrapped span query.</summary>
    public SpanQuery MaskedQuery { get; }

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Initialises a field-masking span query.</summary>
    public FieldMaskingSpanQuery(SpanQuery maskedQuery, string field)
    {
        MaskedQuery = maskedQuery ?? throw new ArgumentNullException(nameof(maskedQuery));
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        Field = field;
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        MaskedQuery.Visit(visitor.GetSubVisitor(Occur.Must, this));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is FieldMaskingSpanQuery other
            && string.Equals(Field, other.Field, StringComparison.Ordinal)
            && MaskedQuery.Equals(other.MaskedQuery)
            && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(FieldMaskingSpanQuery), MaskedQuery, Field));
}
