using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Index.Migration;

/// <summary>
/// Options for codec migration planning and execution.
/// </summary>
public sealed class IndexCodecMigrationOptions
{
    /// <summary>Initialises migration options and captures the current codec catalogue default.</summary>
    public IndexCodecMigrationOptions()
        : this(LeanCorpusDefaults.GetSnapshot())
    {
    }

    internal IndexCodecMigrationOptions(LeanCorpusDefaultSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Catalog = snapshot.Codecs.Catalog.IsSet ? snapshot.Codecs.Catalog.Value : CodecCatalog.Default;
    }

    /// <summary>Gets or sets the immutable codec catalogue used to inspect and validate migration input.</summary>
    public CodecCatalog Catalog { get; set; } = CodecCatalog.Default;

    /// <summary>Gets or sets whether migration should only report actions. Defaults to <c>true</c>.</summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Gets or sets an explicit staging directory path.</summary>
    public string? StagingDirectory { get; set; }

    /// <summary>Gets or sets whether the source index is validated before migration. Defaults to <c>true</c>.</summary>
    public bool ValidateBeforeMigration { get; set; } = true;

    /// <summary>Gets or sets whether the migrated index is validated before publication. Defaults to <c>true</c>.</summary>
    public bool ValidateAfterMigration { get; set; } = true;
}
