namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches documents that contain at least one value for the named field.</summary>
public sealed class FieldExistsQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Initialises a new <see cref="FieldExistsQuery"/>.</summary>
    public FieldExistsQuery(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));

        Field = field;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is FieldExistsQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(FieldExistsQuery), Field));
}
