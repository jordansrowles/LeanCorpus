using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using System;
using System.Buffers;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// A single case in a Choice codec. Created via <see cref="Codec.Case{TBase,TCase}"/>.
/// </summary>
public sealed class CaseDefinition<TBase>
{
    internal CaseDefinition(object tag, string label, Type caseType,
        ICaseHandler<TBase> handler)
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

/// <summary>
/// Internal handler that type-erases the TCase generic parameter.
/// </summary>
internal interface ICaseHandler<TBase>
{
    TBase Decode(ref SequenceReader<byte> reader, CodecContext context);
    bool Matches(TBase value);
    bool MatchesExact(TBase value);
    void Encode(TBase value, IBufferWriter<byte> writer, CodecContext context);
}

internal sealed class CaseHandler<TBase, TCase> : ICaseHandler<TBase> where TCase : TBase
{
    private readonly ICodec<TCase> _codec;

    public CaseHandler(ICodec<TCase> codec)
    {
        _codec = codec;
    }

    public TBase Decode(ref SequenceReader<byte> reader, CodecContext context)
        => _codec.Decode(ref reader, context)!;

    public bool Matches(TBase value) => value is TCase;

    public bool MatchesExact(TBase value) =>
        value is not null && value!.GetType() == typeof(TCase);

    public void Encode(TBase value, IBufferWriter<byte> writer, CodecContext context)
        => _codec.Encode((TCase)value!, writer, context);
}
