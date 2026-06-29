using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// A single case in a Versioned codec. Created via <see cref="Codec.VersionCase{TBase,TCase}"/>.
/// </summary>
public sealed class VersionCaseDefinition<TBase>
{
    internal VersionCaseDefinition(object version, string label, Type caseType,
        ICaseHandler<TBase> handler)
    {
        Version = version;
        Label = label;
        CaseType = caseType;
        Handler = handler;
    }

    internal object Version { get; }

    /// <summary>Label for diagnostic path segments.</summary>
    public string Label { get; }

    internal Type CaseType { get; }
    internal ICaseHandler<TBase> Handler { get; }
}
