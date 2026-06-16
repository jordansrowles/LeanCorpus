using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Version-envelope codec: [version][body-length][body].
/// Known versions decode via their case codec; unknown versions are preserved
/// as raw bytes via a user-supplied delegate.
/// </summary>
internal sealed class VersionEnvelopeCodec<TBase, TVersion> : ICodec<TBase> where TVersion : notnull
{
    private readonly ICodec<TVersion> _versionCodec;
    private readonly ICodec<long> _bodyLengthCodec;
    private readonly Dictionary<TVersion, VersionCaseDefinition<TBase>> _versionToCaseMap;
    private readonly VersionCaseDefinition<TBase>[] _cases;
    private readonly Func<TVersion, byte[], TBase> _unknown;

    public VersionEnvelopeCodec(
        ICodec<TVersion> versionCodec,
        ICodec<long> bodyLengthCodec,
        VersionCaseDefinition<TBase>[] cases,
        Func<TVersion, byte[], TBase> unknown)
    {
        _versionCodec = versionCodec ?? throw new ArgumentNullException(nameof(versionCodec));
        _bodyLengthCodec = bodyLengthCodec ?? throw new ArgumentNullException(nameof(bodyLengthCodec));
        _unknown = unknown ?? throw new ArgumentNullException(nameof(unknown));

        if (cases == null || cases.Length == 0)
            throw new ArgumentException("At least one case must be provided.", nameof(cases));

        var versionSet = new HashSet<TVersion>();
        foreach (var c in cases)
        {
            if (!versionSet.Add((TVersion)c.Version))
                throw new ArgumentException($"Duplicate version: {c.Version}");
        }

        _cases = cases;
        _versionToCaseMap = cases.ToDictionary(c => (TVersion)c.Version);
    }

    public TBase Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        try
        {
            using var envelopePathGuard = context.PushPath("{envelope}");

            TVersion version = _versionCodec.Decode(ref reader, context);
            long bodyLength = _bodyLengthCodec.Decode(ref reader, context);

            if (bodyLength < 0)
                throw new InvalidValueException(
                    CodecErrorCode.NegativeLength,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    $"Negative body length: {bodyLength}");

            if (bodyLength > context.Options.MaxFrameBytes)
                throw new LimitExceededException(
                    CodecErrorCode.FrameTooLarge,
                    context.GetByteOffset(ref reader),
                    context.CurrentPath,
                    "BodyLength", bodyLength, context.Options.MaxFrameBytes);

            int bodyLen = (int)bodyLength;
            if (reader.Remaining < bodyLen)
                throw new InsufficientDataException(
                    context.GetByteOffset(ref reader),
                    context.CurrentPath, bodyLen, (int)reader.Remaining);

            if (_versionToCaseMap.TryGetValue(version, out var caseDef))
            {
                // Known version: decode via case codec with exact consumption enforcement
                var bodySequence = reader.Sequence.Slice(reader.Position, bodyLen);
                var bodyReader = new SequenceReader<byte>(bodySequence);

                using var pathGuard = context.PushPath($"<{caseDef.Label}>");
                using var scope = context.EnterScope(bodyLen);
                using var depthGuard = context.PushDepth();
                TBase value = caseDef.Handler.Decode(ref bodyReader, context);

                if (bodyReader.Remaining > 0)
                {
                    throw new TrailingDataException(
                        context.GetByteOffset(ref reader) + bodyReader.Consumed,
                        context.CurrentPath,
                        bodyReader.Remaining);
                }

                reader.Advance(bodyLen);
                return value;
            }
            else
            {
                // Unknown version: read body bytes as raw, pass to delegate
                byte[] bodyBytes = new byte[bodyLen];
                if (!reader.TryCopyTo(bodyBytes))
                    throw new InsufficientDataException(
                        context.GetByteOffset(ref reader),
                        context.CurrentPath, bodyLen, (int)reader.Remaining);
                reader.Advance(bodyLen);

                return _unknown(version, bodyBytes);
            }
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
    }

    public void Encode(TBase value, IBufferWriter<byte> writer, CodecContext context)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));


        // Find matching case — exact type first, then pattern match
        VersionCaseDefinition<TBase>? matchedCase = null;

        foreach (var caseDef in _cases)
        {
            if (caseDef.Handler.MatchesExact(value))
            {
                matchedCase = caseDef;
                break;
            }
        }

        if (matchedCase == null)
        {
            foreach (var caseDef in _cases)
            {
                if (caseDef.Handler.Matches(value))
                {
                    matchedCase = caseDef;
                    break;
                }
            }
        }

        if (matchedCase == null)
        {
            var registeredTypes = string.Join(", ", Array.ConvertAll(_cases, c => c.CaseType.Name));
            throw new InvalidValueException(
                CodecErrorCode.InvalidValue, 0, context.CurrentPath,
                $"No matching version case for type {value.GetType().Name}. Registered cases: {registeredTypes}");
        }

        // Stage inner encode to scratch buffer to measure body length
        var scratch = context.RentScratchBuffer();
        try
        {
            using var pathGuard = context.PushPath($"<{matchedCase.Label}>");
            using var depthGuard = context.PushDepth();
            matchedCase.Handler.Encode(value, scratch, context);

            long bodyLength = scratch.Length;

            // Write version
            _versionCodec.Encode((TVersion)matchedCase.Version, writer, context);

            // Write body length
            _bodyLengthCodec.Encode(bodyLength, writer, context);

            // Write body bytes
            var written = scratch.Written;
            foreach (var segment in written)
            {
                var span = writer.GetSpan(segment.Length);
                segment.Span.CopyTo(span);
                writer.Advance(segment.Length);
            }
        }
        finally
        {
            context.ReturnScratchBuffer(scratch);
        }
    }

    /// <summary>
    /// Fast-path encode for <see cref="ReadOnlySpan{Byte}"/> payloads.
    /// Writes version + body-length + body bytes directly without staging
    /// through a scratch buffer, since the body length is already known.
    /// Uses the first (newest) version case — callers must ensure the
    /// payload is compatible with the current format version.
    /// </summary>
    internal void EncodeSpan(ReadOnlySpan<byte> body, IBufferWriter<byte> writer, CodecContext context)
    {
        if (_cases.Length == 0)
            throw new InvalidOperationException("No version cases registered.");

        var currentCase = _cases[0];

        // Write version
        _versionCodec.Encode((TVersion)currentCase.Version, writer, context);

        // Write body length
        _bodyLengthCodec.Encode(body.Length, writer, context);

        // Write body bytes directly — bypass scratch staging
        if (body.Length > 0)
        {
            var span = writer.GetSpan(body.Length);
            body.CopyTo(span);
            writer.Advance(body.Length);
        }
    }
}
