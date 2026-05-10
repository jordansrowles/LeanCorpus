using Rowles.LeanLucene.Index.Format;
using Rowles.LeanLucene.Index.Migration;

namespace Rowles.LeanLucene.Index.Compatibility;

/// <summary>
/// Result of a LeanLucene index compatibility check.
/// </summary>
public sealed record IndexCompatibilityResult
{
    /// <summary>Gets the compatibility status.</summary>
    public required IndexCompatibilityStatus Status { get; init; }

    /// <summary>Gets a value indicating whether the index can be read by this build.</summary>
    public required bool CanRead { get; init; }

    /// <summary>Gets a value indicating whether the index can be written by this build without migration.</summary>
    public required bool CanWrite { get; init; }

    /// <summary>Gets a value indicating whether this build can run validation checks for the index.</summary>
    public required bool CanValidate { get; init; }

    /// <summary>Gets a value indicating whether all planned migration actions can execute.</summary>
    public required bool CanMigrate { get; init; }

    /// <summary>Gets a value indicating whether the index must be rejected by this build.</summary>
    public required bool MustReject { get; init; }

    /// <summary>Gets a value indicating whether migration is required before this operation can continue.</summary>
    public required bool RequiresMigration { get; init; }

    /// <summary>Gets the index format inventory used to decide compatibility.</summary>
    public required IndexFormatInventory Inventory { get; init; }

    /// <summary>Gets compatibility and validation issues.</summary>
    public required IReadOnlyList<IndexCheckIssue> Issues { get; init; }

    /// <summary>Gets planned migration actions.</summary>
    public required IReadOnlyList<IndexCodecMigrationAction> MigrationActions { get; init; }
}
