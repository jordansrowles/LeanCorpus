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
    DeleteTemporaryFile = 5,

    /// <summary>A legacy body can be streamed unchanged into its current canonical frame.</summary>
    Reframe = 6,

    /// <summary>A logical file must be decoded and written through its current writer.</summary>
    Rewrite = 7,

    /// <summary>A coordinated codec family must be rewritten as one logical action.</summary>
    CoordinatedRewrite = 8,

    /// <summary>A compound segment must be repacked after its logical members are migrated.</summary>
    RepackCompound = 9,

    /// <summary>The format is inspectable but has no executable migration path.</summary>
    Unsupported = 10
}
