namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Requests orderly removal of a node from serving work.</summary>
public sealed record DrainNodeRequest(string NodeId, TimeSpan? Timeout = null);
