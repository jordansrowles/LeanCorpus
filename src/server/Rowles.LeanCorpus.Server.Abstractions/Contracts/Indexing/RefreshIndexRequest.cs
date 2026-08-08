namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Requests a visibility refresh.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
public sealed record RefreshIndexRequest(string IndexName);
