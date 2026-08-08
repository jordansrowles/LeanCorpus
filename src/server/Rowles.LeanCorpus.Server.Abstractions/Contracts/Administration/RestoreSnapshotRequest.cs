namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Requests restoration of a snapshot into explicitly named indices.</summary>
public sealed record RestoreSnapshotRequest(string SnapshotId, IReadOnlyDictionary<string, string>? IndexNameMapping = null);
