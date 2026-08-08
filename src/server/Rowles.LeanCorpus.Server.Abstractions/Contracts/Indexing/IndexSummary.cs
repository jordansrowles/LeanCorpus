namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Summarises a logical index without exposing its physical path.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="IndexId">Opaque stable index identifier.</param>
/// <param name="SchemaHash">Stable hash of the immutable schema.</param>
/// <param name="DocumentCount">Visible document count.</param>
/// <param name="CreatedUtc">UTC creation time.</param>
public sealed record IndexSummary(
    string IndexName,
    string IndexId,
    string SchemaHash,
    long DocumentCount,
    DateTimeOffset CreatedUtc);
