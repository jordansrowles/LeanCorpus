namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Reports a snapshot restore operation.</summary>
public sealed record RestoreSnapshotResponse(string SnapshotId, string State);
