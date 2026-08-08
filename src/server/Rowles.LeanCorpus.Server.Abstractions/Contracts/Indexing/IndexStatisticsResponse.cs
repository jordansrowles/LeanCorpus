namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Returns local or distributed index statistics.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="SchemaHash">Stable schema hash.</param>
/// <param name="DocumentCount">Visible document count.</param>
/// <param name="DeletedDocumentCount">Deleted document count awaiting reclamation.</param>
/// <param name="StorageBytes">Current on-disk byte count.</param>
/// <param name="SegmentCount">Current segment count.</param>
/// <param name="CommitGeneration">Latest visible commit generation.</param>
public sealed record IndexStatisticsResponse(
    string IndexName,
    string SchemaHash,
    long DocumentCount,
    long DeletedDocumentCount,
    long StorageBytes,
    int SegmentCount,
    long CommitGeneration);
