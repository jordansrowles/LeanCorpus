namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Returns the generation made visible by a refresh.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="CommitGeneration">Visible commit generation.</param>
public sealed record RefreshIndexResponse(string IndexName, long CommitGeneration);
