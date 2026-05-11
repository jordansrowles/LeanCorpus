namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// State recorded by an index migration marker.
/// </summary>
public enum IndexMigrationState
{
    /// <summary>No migration marker exists.</summary>
    None = 0,

    /// <summary>Migration has been prepared but no file rewrite has started.</summary>
    Prepared = 1,

    /// <summary>Migration file rewrites are in progress.</summary>
    InProgress = 2,

    /// <summary>Migration output has validated and is ready to publish.</summary>
    ReadyToPublish = 3,

    /// <summary>Migration has been published.</summary>
    Published = 4,

    /// <summary>Migration failed and requires recovery action.</summary>
    Failed = 5
}
