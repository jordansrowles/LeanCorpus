namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>No pending writes required publication.</summary>
public sealed record NothingToCommit;

/// <summary>A commit was durably published.</summary>
public sealed record CommitPublished(LocalCommitReceipt Receipt);

/// <summary>A commit attempt failed before publication.</summary>
public sealed record CommitFailed(string Message, Exception? Exception = null);

/// <summary>Represents the mutually exclusive outcome of a local commit request.</summary>
public union CommitResult(NothingToCommit, CommitPublished, CommitFailed);
