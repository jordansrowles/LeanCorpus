namespace Rowles.LeanCorpus.Index.Compatibility;

/// <summary>
/// Options for compatibility checks.
/// </summary>
public sealed class IndexCompatibilityOptions
{
    /// <summary>Gets or sets whether deep validation should run before compatibility is decided.</summary>
    public bool DeepValidation { get; set; }

    /// <summary>Gets or sets whether readable older formats are allowed. Defaults to <c>true</c>.</summary>
    public bool AllowSupportedOlderFormats { get; set; } = true;

    /// <summary>Gets or sets whether every codec file must already be at the current version.</summary>
    public bool RequireCurrentFormats { get; set; }
}
