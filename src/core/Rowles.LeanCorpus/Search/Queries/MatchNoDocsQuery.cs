namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches no documents.</summary>
public sealed class MatchNoDocsQuery : Query
{
    /// <summary>Gets the optional reason describing why no documents should match.</summary>
    public string Reason { get; }

    /// <inheritdoc/>
    public override string Field => string.Empty;

    /// <summary>Initialises a new <see cref="MatchNoDocsQuery"/>.</summary>
    public MatchNoDocsQuery(string reason = "")
    {
        ArgumentNullException.ThrowIfNull(reason);
        Reason = reason;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is MatchNoDocsQuery other &&
        string.Equals(Reason, other.Reason, StringComparison.Ordinal) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(MatchNoDocsQuery), Reason));
}
