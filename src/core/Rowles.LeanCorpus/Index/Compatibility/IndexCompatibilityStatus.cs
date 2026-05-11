namespace Rowles.LeanCorpus.Index.Compatibility;

/// <summary>
/// Compatibility state for a LeanCorpus index.
/// </summary>
public enum IndexCompatibilityStatus
{
    /// <summary>The directory has no committed index.</summary>
    Empty = 0,

    /// <summary>The index is compatible with this build.</summary>
    Compatible = 1,

    /// <summary>The index can be read but should be migrated.</summary>
    MigrationRecommended = 2,

    /// <summary>The index must be migrated before this operation can continue.</summary>
    MigrationRequired = 3,

    /// <summary>The index contains a future format version unsupported by this build.</summary>
    UnsupportedFutureFormat = 4,

    /// <summary>The index is corrupt or structurally invalid.</summary>
    Corrupt = 5
}
