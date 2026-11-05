namespace Rowles.LeanCorpus.Codecs.CodecKit;

/// <summary>Identifies the operation requested on a persisted representation.</summary>
public enum CodecLifecycleOperation
{
    Read,
    Create,
    RewriteSource,
    Migrate,
}

/// <summary>A body identity obtained from validated codec metadata.</summary>
public readonly record struct CodecRepresentationIdentity(string FormatId, int Version, string? RepresentationId = null);

/// <summary>The stable outcome of a codec lifecycle check.</summary>
public readonly record struct CodecLifecycleDecision(bool IsAllowed, string ReasonCode, string Explanation)
{
    public static CodecLifecycleDecision Allowed => new(true, "Allowed", string.Empty);

    public static CodecLifecycleDecision Denied(string reasonCode, string explanation)
        => new(false, reasonCode, explanation);
}

/// <summary>
/// Narrows descriptor capabilities for semantic representations and declares permitted migration routes.
/// Implementations must reject unknown or missing representation identities and remain immutable.
/// </summary>
public interface ICodecLifecyclePolicy
{
    CodecLifecycleDecision Evaluate(
        CodecLifecycleOperation operation,
        CodecRepresentationIdentity? source,
        CodecRepresentationIdentity? target);
}
