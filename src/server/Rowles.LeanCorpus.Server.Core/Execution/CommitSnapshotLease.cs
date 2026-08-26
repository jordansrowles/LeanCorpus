using Rowles.LeanCorpus.Index.Backup;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Pins one immutable local commit while its manifest files are consumed.</summary>
public abstract class CommitSnapshotLease : IAsyncDisposable, IDisposable
{
    /// <summary>Gets the pinned commit generation.</summary>
    public abstract long CommitGeneration { get; }

    /// <summary>Gets the pinned commit content token.</summary>
    public abstract long ContentToken { get; }

    /// <summary>Gets the validated manifest for the pinned generation.</summary>
    public abstract IndexBackupManifest Manifest { get; }

    /// <summary>Opens one manifest file for reading.</summary>
    public abstract Stream OpenRead(string fileName);

    /// <inheritdoc />
    public abstract void Dispose();

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
