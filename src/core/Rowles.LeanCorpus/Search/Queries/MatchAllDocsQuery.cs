namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches every live document in the index.</summary>
public sealed class MatchAllDocsQuery : Query
{
    /// <inheritdoc/>
    public override string Field => string.Empty;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is MatchAllDocsQuery other && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(nameof(MatchAllDocsQuery).GetHashCode(StringComparison.Ordinal));
}
