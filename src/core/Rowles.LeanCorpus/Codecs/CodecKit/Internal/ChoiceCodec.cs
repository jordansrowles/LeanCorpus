using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Tag-based discriminated union codec.
/// Decode: read tag → lookup case → decode body → upcast.
/// Encode: pattern-match value type → write tag → encode body.
/// </summary>
internal sealed class ChoiceCodec<TBase, TTag> : ICodec<TBase> where TTag : notnull
{
    private readonly ICodec<TTag> _tagCodec;
    private readonly Dictionary<TTag, CaseDefinition<TBase>> _tagToCaseMap;
    private readonly CaseDefinition<TBase>[] _cases;

    public ChoiceCodec(ICodec<TTag> tagCodec, params CaseDefinition<TBase>[] cases)
    {
        _tagCodec = tagCodec ?? throw new ArgumentNullException(nameof(tagCodec));
        if (cases == null || cases.Length == 0)
            throw new ArgumentException("At least one case must be provided.", nameof(cases));

        // Validate: no duplicate tags
        var tagSet = new HashSet<TTag>();
        var typeSet = new HashSet<Type>();
        foreach (var c in cases)
        {
            if (!tagSet.Add((TTag)c.Tag))
                throw new ArgumentException($"Duplicate tag: {c.Tag}");
            if (!typeSet.Add(c.CaseType))
                throw new ArgumentException($"Duplicate case type: {c.CaseType.Name}");
        }

        _cases = cases;
        _tagToCaseMap = cases.ToDictionary(c => (TTag)c.Tag);
    }

    public TBase Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            TTag tag = _tagCodec.Decode(ref reader, context);

            if (!_tagToCaseMap.TryGetValue(tag, out var caseDef))
            {
                throw new UnknownDiscriminatorException(
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    tag?.ToString() ?? "(null)");
            }

            using var pathGuard = context.PushPath($"<{caseDef.Label}>");
            using var depthGuard = context.PushDepth();
return caseDef.Handler.Decode(ref reader, context);
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(TBase value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value));

        // Exact runtime type first (amendment #5)
        foreach (var caseDef in _cases)
        {
            if (caseDef.Handler.MatchesExact(value))
            {
                _tagCodec.Encode((TTag)caseDef.Tag, writer, context);
                using var depthGuard = context.PushDepth();
                caseDef.Handler.Encode(value, writer, context);
                return;
            }
        }

        // Fall back to `is TCase` pattern matching
        foreach (var caseDef in _cases)
        {
            if (caseDef.Handler.Matches(value))
            {
                _tagCodec.Encode((TTag)caseDef.Tag, writer, context);
                using var depthGuard = context.PushDepth();
                caseDef.Handler.Encode(value, writer, context);
                return;
            }
        }

        var registeredTypes = string.Join(", ", Array.ConvertAll(_cases, c => c.CaseType.Name));
        throw new CodecValidationException(
            CodecErrorCode.InvalidValue, 0, context.CurrentPath,
            $"No matching case for type {value.GetType().Name}. Registered cases: {registeredTypes}");
    }
}
