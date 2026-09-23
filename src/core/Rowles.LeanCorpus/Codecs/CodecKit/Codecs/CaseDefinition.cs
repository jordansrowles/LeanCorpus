using System;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>A single case in a Choice codec, created via <see cref="Codec.Case{TBase,TCase}"/>.</summary>
public sealed class CaseDefinition<TBase>
{
    internal CaseDefinition(object tag, string label, Type caseType, ICaseHandler<TBase> handler)
    {
        Tag = tag;
        Label = label;
        CaseType = caseType;
        Handler = handler;
    }

    internal object Tag { get; }

    /// <summary>Label for diagnostic path segments.</summary>
    public string Label { get; }

    internal Type CaseType { get; }
    internal ICaseHandler<TBase> Handler { get; }
}
