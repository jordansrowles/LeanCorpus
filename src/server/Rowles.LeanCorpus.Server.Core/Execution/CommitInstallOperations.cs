using Rowles.LeanCorpus.Index.Backup;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Provides the copy and directory operations used by local commit installation.</summary>
internal interface ICommitInstallOperations
{
    void Materialise(
        CommitSnapshotLease lease,
        string backupDirectoryPath,
        string materialisedDirectoryPath,
        CancellationToken cancellationToken);

    void MoveDirectory(string sourcePath, string destinationPath);
}

/// <summary>Allows a pinned runtime snapshot to be copied without exposing engine objects.</summary>
internal interface ICommitSnapshotSource
{
    void CreateBackup(string backupDirectoryPath, CancellationToken cancellationToken);
}

/// <summary>Default local commit materialisation and publication operations.</summary>
internal sealed class DefaultCommitInstallOperations : ICommitInstallOperations
{
    internal static DefaultCommitInstallOperations Instance { get; } = new();

    public void Materialise(
        CommitSnapshotLease lease,
        string backupDirectoryPath,
        string materialisedDirectoryPath,
        CancellationToken cancellationToken)
    {
        if (lease is not ICommitSnapshotSource source)
            throw new InvalidOperationException("The commit snapshot does not belong to a local runtime.");

        source.CreateBackup(backupDirectoryPath, cancellationToken);
        IndexBackup.Restore(
            backupDirectoryPath,
            materialisedDirectoryPath,
            new IndexRestoreOptions { OverwriteTargetDirectory = false, ValidateAfterRestore = true },
            cancellationToken);
    }

    public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);
}
