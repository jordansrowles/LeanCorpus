namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Contains a transport-neutral score explanation.</summary>
public sealed record ExplainResponse(bool IsMatch, float? Score, string Description, IReadOnlyList<ExplainResponse>? Details = null);
