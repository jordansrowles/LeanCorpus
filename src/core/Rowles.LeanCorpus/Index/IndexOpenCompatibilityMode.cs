namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Controls compatibility checks performed before opening an index.
/// </summary>
public enum IndexOpenCompatibilityMode
{
    /// <summary>Reject corrupt indexes, unsupported future formats, and migration-required indexes.</summary>
    Strict = 0,

    /// <summary>Allow readable older formats while still rejecting corrupt and future formats.</summary>
    AllowSupportedOlderFormats = 1,

    /// <summary>Skip compatibility guardrails. This mode is intended only for diagnostics and recovery tooling.</summary>
    UnsafeIgnoreCompatibility = 2
}
