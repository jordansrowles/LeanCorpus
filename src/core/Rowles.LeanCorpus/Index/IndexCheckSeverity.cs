namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Severity of an index validation issue.
/// </summary>
public enum IndexCheckSeverity
{
    /// <summary>Informational diagnostic that does not affect index health.</summary>
    Info = 0,

    /// <summary>Non-fatal issue that should be reviewed.</summary>
    Warning = 1,

    /// <summary>Fatal issue that means the checked index is not healthy.</summary>
    Error = 2
}
