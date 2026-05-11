namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Options for codec migration planning and execution.
/// </summary>
public sealed class IndexCodecMigrationOptions
{
    /// <summary>Gets or sets whether migration should only report actions. Defaults to <c>true</c>.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Gets or sets whether migration should run in a staging directory. Defaults to <c>true</c>.</summary>
    public bool UseStagingDirectory { get; set; } = true;

    /// <summary>Gets or sets an explicit staging directory path.</summary>
    public string? StagingDirectory { get; set; }

    /// <summary>Gets or sets whether the source index is validated before migration. Defaults to <c>true</c>.</summary>
    public bool ValidateBeforeMigration { get; set; } = true;

    /// <summary>Gets or sets whether the migrated index is validated before publication. Defaults to <c>true</c>.</summary>
    public bool ValidateAfterMigration { get; set; } = true;

    /// <summary>Gets or sets whether in-place migration is allowed.</summary>
    public bool AllowInPlaceMigration { get; set; }
}
