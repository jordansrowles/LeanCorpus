using System.Net;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches IPv4 or IPv6 values within an inclusive or exclusive address range.</summary>
public sealed class InetAddressRangeQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the lower address bound.</summary>
    public IPAddress Lower { get; }

    /// <summary>Gets the upper address bound.</summary>
    public IPAddress Upper { get; }

    /// <summary>Gets whether the lower bound is inclusive.</summary>
    public bool IncludeLower { get; }

    /// <summary>Gets whether the upper bound is inclusive.</summary>
    public bool IncludeUpper { get; }

    /// <summary>Initialises an IP address range query.</summary>
    public InetAddressRangeQuery(
        string field,
        IPAddress lower,
        IPAddress upper,
        bool includeLower = true,
        bool includeUpper = true)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field must be a non-empty value.", nameof(field));
        ArgumentNullException.ThrowIfNull(lower);
        ArgumentNullException.ThrowIfNull(upper);

        Field = field;
        Lower = lower;
        Upper = upper;
        IncludeLower = includeLower;
        IncludeUpper = includeUpper;
    }

    /// <inheritdoc/>
    public override Query Rewrite()
    {
        var rewritten = new BinaryRangeQuery(
            Field,
            InetAddressEncoding.Encode(Lower),
            InetAddressEncoding.Encode(Upper),
            IncludeLower,
            IncludeUpper)
        {
            Boost = Boost
        };
        return rewritten;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is InetAddressRangeQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Lower.Equals(other.Lower) &&
        Upper.Equals(other.Upper) &&
        IncludeLower == other.IncludeLower &&
        IncludeUpper == other.IncludeUpper &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() =>
        CombineBoost(HashCode.Combine(
            nameof(InetAddressRangeQuery),
            Field,
            Lower,
            Upper,
            IncludeLower,
            IncludeUpper));
}
