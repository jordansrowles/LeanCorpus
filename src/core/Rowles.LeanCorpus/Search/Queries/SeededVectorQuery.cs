namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// Vector query with deterministic global document-ID seeds for HNSW traversal.
/// </summary>
/// <remarks>
/// Seeds are hints only. Invalid, deleted, non-vector, and out-of-segment seeds are
/// ignored, and the ordinary graph entry point remains a fallback.
/// </remarks>
public sealed class SeededVectorQuery : VectorQuery
{
    private readonly int[] _seedDocumentIds;

    /// <summary>Global document identifiers considered as extra traversal entries.</summary>
    public IReadOnlyList<int> SeedDocumentIds => _seedDocumentIds;

    /// <summary>Initialises a vector query with bounded deterministic traversal seeds.</summary>
    public SeededVectorQuery(
        string field,
        float[] queryVector,
        IEnumerable<int> seedDocumentIds,
        int topK = 10,
        int efSearch = 0,
        int oversamplingFactor = 1,
        Query? filter = null,
        int maxVisitedNodes = 0)
        : base(
            field,
            queryVector,
            topK,
            efSearch,
            oversamplingFactor,
            filter,
            maxVisitedNodes)
    {
        ArgumentNullException.ThrowIfNull(seedDocumentIds);
        _seedDocumentIds = seedDocumentIds
            .Distinct()
            .Order()
            .ToArray();
        if (_seedDocumentIds.Any(static id => id < 0))
            throw new ArgumentOutOfRangeException(
                nameof(seedDocumentIds),
                "Seed document identifiers must be non-negative.");
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SeededVectorQuery other &&
        base.Equals((object)other) &&
        _seedDocumentIds.AsSpan().SequenceEqual(other._seedDocumentIds);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(base.GetHashCode());
        foreach (int seed in _seedDocumentIds)
            hash.Add(seed);
        return hash.ToHashCode();
    }
}
