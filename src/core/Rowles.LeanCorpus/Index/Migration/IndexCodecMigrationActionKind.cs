namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Describes the kind of file-system action required by a codec migration.
/// </summary>
public enum IndexCodecMigrationActionKind
{
    /// <summary>No file-system change is required.</summary>
    NoOp = 0,

    /// <summary>A codec file must be rewritten to the current version.</summary>
    RewriteFile = 1,

    /// <summary>A file must be copied into the migration target.</summary>
    CopyFile = 2,

    /// <summary>A commit must be published after migration.</summary>
    PublishCommit = 3,

    /// <summary>A migration marker must be written.</summary>
    WriteMarker = 4,

    /// <summary>A temporary file must be deleted.</summary>
    DeleteTemporaryFile = 5
}
