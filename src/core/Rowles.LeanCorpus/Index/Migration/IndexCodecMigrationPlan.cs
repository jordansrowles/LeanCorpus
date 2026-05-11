using Rowles.LeanCorpus.Index.Format;

namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Describes the codec migration actions required for an index.
/// </summary>
public sealed record IndexCodecMigrationPlan
{
    /// <summary>Gets the inspected index inventory.</summary>
    public required IndexFormatInventory Inventory { get; init; }

    /// <summary>Gets planned migration actions.</summary>
    public required IReadOnlyList<IndexCodecMigrationAction> Actions { get; init; }

    /// <summary>Gets a value indicating whether every planned action can execute.</summary>
    public required bool CanExecute { get; init; }

    /// <summary>Gets issues found while building the migration plan.</summary>
    public required IReadOnlyList<IndexCheckIssue> Issues { get; init; }
}
