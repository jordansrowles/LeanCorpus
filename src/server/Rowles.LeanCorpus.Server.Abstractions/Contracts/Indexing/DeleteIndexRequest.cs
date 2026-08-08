namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Requests deletion of a logical index.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="ConfirmationToken">Required server-issued confirmation token.</param>
public sealed record DeleteIndexRequest(string IndexName, string ConfirmationToken);
