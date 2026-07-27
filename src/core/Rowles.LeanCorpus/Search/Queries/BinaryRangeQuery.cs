namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches binary doc values within a lexicographic byte range.</summary>
public sealed class BinaryRangeQuery : Query
{
    private readonly byte[]? _lower;
    private readonly byte[]? _upper;

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the lower bound, or an empty value when unbounded.</summary>
    public ReadOnlyMemory<byte>? Lower => _lower;

    /// <summary>Gets the upper bound, or an empty value when unbounded.</summary>
    public ReadOnlyMemory<byte>? Upper => _upper;

    /// <summary>Gets whether the lower bound is inclusive.</summary>
    public bool IncludeLower { get; }

    /// <summary>Gets whether the upper bound is inclusive.</summary>
    public bool IncludeUpper { get; }

    /// <summary>Initialises a binary range query.</summary>
    public BinaryRangeQuery(
        string field,
        ReadOnlyMemory<byte>? lower,
        ReadOnlyMemory<byte>? upper,
        bool includeLower = true,
        bool includeUpper = true)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));

        Field = field;
        _lower = lower?.ToArray();
        _upper = upper?.ToArray();
        IncludeLower = includeLower;
        IncludeUpper = includeUpper;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is BinaryRangeQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        IncludeLower == other.IncludeLower &&
        IncludeUpper == other.IncludeUpper &&
        Boost == other.Boost &&
        NullableSequenceEqual(_lower, other._lower) &&
        NullableSequenceEqual(_upper, other._upper);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(nameof(BinaryRangeQuery));
        hash.Add(Field);
        hash.Add(IncludeLower);
        hash.Add(IncludeUpper);
        if (_lower is not null)
            hash.AddBytes(_lower);
        if (_upper is not null)
            hash.AddBytes(_upper);
        return CombineBoost(hash.ToHashCode());
    }

    private static bool NullableSequenceEqual(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
}
