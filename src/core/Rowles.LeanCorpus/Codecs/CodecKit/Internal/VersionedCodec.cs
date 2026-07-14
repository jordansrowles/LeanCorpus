using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Version-based discriminated union codec.
/// Decode: read version → lookup case → decode body → upcast.
/// Encode: pattern-match value type → write version → encode body.
/// </summary>
internal sealed class VersionedCodec<TBase, TVersion> : ICodec<TBase> where TVersion : notnull
{
    private readonly ICodec<TVersion> _versionCodec;
    private readonly Dictionary<TVersion, VersionCaseDefinition<TBase>> _versionToCaseMap;
    private readonly VersionCaseDefinition<TBase>[] _cases;

    public VersionedCodec(ICodec<TVersion> versionCodec, params VersionCaseDefinition<TBase>[] cases)
    {
        _versionCodec = versionCodec ?? throw new ArgumentNullException(nameof(versionCodec));
        if (cases == null || cases.Length == 0)
            throw new ArgumentException("At least one case must be provided.", nameof(cases));

        var versionSet = new HashSet<TVersion>();
        var typeSet = new HashSet<Type>();
        foreach (var c in cases)
        {
            if (!versionSet.Add((TVersion)c.Version))
                throw new ArgumentException($"Duplicate version: {c.Version}");
            if (!typeSet.Add(c.CaseType))
                throw new ArgumentException($"Duplicate case type: {c.CaseType.Name}");
        }

        _cases = cases;
        _versionToCaseMap = cases.ToDictionary(c => (TVersion)c.Version);
    }

    public TBase Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            TVersion version = _versionCodec.Decode(ref reader, context);

            if (!_versionToCaseMap.TryGetValue(version, out var caseDef))
            {
                throw new UnknownVersionException(
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    version);
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

        // Exact runtime type first
        foreach (var caseDef in _cases)
        {
            if (caseDef.Handler.MatchesExact(value))
            {
                _versionCodec.Encode((TVersion)caseDef.Version, writer, context);
                using var depthGuard = context.PushDepth();
                caseDef.Handler.Encode(value, writer, context);
                return;
            }
        }

        // Fall back to pattern matching
        foreach (var caseDef in _cases)
        {
            if (caseDef.Handler.Matches(value))
            {
                _versionCodec.Encode((TVersion)caseDef.Version, writer, context);
                using var depthGuard = context.PushDepth();
                caseDef.Handler.Encode(value, writer, context);
                return;
            }
        }

        var registeredTypes = string.Join(", ", Array.ConvertAll(_cases, c => c.CaseType.Name));
        throw new CodecValidationException(
            CodecErrorCode.InvalidValue, 0, context.CurrentPath,
            $"No matching version case for type {value.GetType().Name}. Registered cases: {registeredTypes}");
    }
}
