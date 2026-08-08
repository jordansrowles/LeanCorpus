namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Reports a snapshot operation.</summary>
public sealed record SnapshotResponse(string SnapshotId, string State);
