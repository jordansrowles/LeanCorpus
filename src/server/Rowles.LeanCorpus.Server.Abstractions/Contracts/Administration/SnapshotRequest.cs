namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Requests creation of a snapshot using a configured private repository.</summary>
public sealed record SnapshotRequest(IReadOnlyList<string> IndexNames, string? Name = null);
