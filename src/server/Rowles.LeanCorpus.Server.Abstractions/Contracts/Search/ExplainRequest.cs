namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Requests an explanation for one document and query.</summary>
public sealed record ExplainRequest(string DocumentId, QueryDefinition Query);
