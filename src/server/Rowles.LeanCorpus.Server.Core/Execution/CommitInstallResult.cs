namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>A commit was atomically installed.</summary>
public sealed record CommitInstalled(LocalCommitReceipt Receipt);

/// <summary>The same generation was already visible.</summary>
public sealed record CommitAlreadyPresent(long Generation);

/// <summary>An install was rejected before publication.</summary>
public sealed record CommitRejected(string Message);

/// <summary>Represents the mutually exclusive result of installing a committed snapshot.</summary>
public union CommitInstallResult(CommitInstalled, CommitAlreadyPresent, CommitRejected);
