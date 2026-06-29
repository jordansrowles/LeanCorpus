using System;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    /// <summary>
    /// Creates a version case definition for use in <see cref="Versioned{TBase,TVersion}"/>
    /// or <see cref="VersionEnvelope{TBase,TVersion}"/>.
    /// </summary>
    public static VersionCaseDefinition<TBase> VersionCase<TBase, TCase>(
        object version, string label, ICodec<TCase> codec) where TCase : TBase
        => new VersionCaseDefinition<TBase>(version, label, typeof(TCase),
            new CaseHandler<TBase, TCase>(codec));

    /// <summary>
    /// Version-based discriminated union codec. Decode reads the version,
    /// dispatches to the matching case codec; encode pattern-matches the value type.
    /// </summary>
    public static ICodec<TBase> Versioned<TBase, TVersion>(
        ICodec<TVersion> versionCodec,
        params VersionCaseDefinition<TBase>[] cases) where TVersion : notnull
        => new VersionedCodec<TBase, TVersion>(versionCodec, cases);

    /// <summary>
    /// Version-envelope codec: [version][body-length][body].
    /// Known versions decode via their case codec; unknown versions are preserved
    /// via the <paramref name="unknown"/> delegate.
    /// </summary>
    public static ICodec<TBase> VersionEnvelope<TBase, TVersion>(
        ICodec<TVersion> versionCodec,
        ICodec<long> bodyLengthCodec,
        Func<TVersion, byte[], TBase> unknown,
        params VersionCaseDefinition<TBase>[] cases) where TVersion : notnull
        => new VersionEnvelopeCodec<TBase, TVersion>(versionCodec, bodyLengthCodec, cases, unknown);
}
