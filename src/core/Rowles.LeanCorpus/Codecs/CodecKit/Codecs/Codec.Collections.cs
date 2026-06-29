using System;
using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    /// <summary>
    /// Decode/encode a fixed number of <typeparamref name="T"/> items.
    /// Path segment: <c>[index]</c> for each element.
    /// </summary>
    public static ICodec<IReadOnlyList<T>> Repeat<T>(this ICodec<T> elementCodec, int count)
        => new RepeatCodec<T>(elementCodec, count);

    /// <summary>
    /// Decode/encode a count-prefixed list with an int count codec.
    /// The count is read/written first, then that many elements follow.
    /// </summary>
    public static ICodec<IReadOnlyList<T>> RepeatPrefixed<T>(this ICodec<T> elementCodec, ICodec<int> countCodec)
        => new RepeatPrefixedCodec<T>(elementCodec, new NumericToLongCodec<int>(countCodec, v => v, v => (int)v));

    /// <summary>
    /// Decode/encode a count-prefixed list with a uint count codec.
    /// </summary>
    public static ICodec<IReadOnlyList<T>> RepeatPrefixed<T>(this ICodec<T> elementCodec, ICodec<uint> countCodec)
        => new RepeatPrefixedCodec<T>(elementCodec, new NumericToLongCodec<uint>(countCodec, v => v, v => (uint)v));

    /// <summary>
    /// Decode/encode a count-prefixed list with a long count codec.
    /// </summary>
    public static ICodec<IReadOnlyList<T>> RepeatPrefixed<T>(this ICodec<T> elementCodec, ICodec<long> countCodec)
        => new RepeatPrefixedCodec<T>(elementCodec, countCodec);

    /// <summary>
    /// Flag-based optional: a bool discriminator precedes the body.
    /// true → body present, false → absent (returns default).
    /// </summary>
    public static ICodec<T?> Optional<T>(this ICodec<T> innerCodec, ICodec<bool> flagCodec) where T : class
        => new OptionalCodec<T>(innerCodec, flagCodec);

    /// <summary>
    /// Sentinel-based optional: if the decoded sentinel equals <paramref name="absentValue"/>,
    /// the result is default/null. Otherwise the full body is decoded.
    /// </summary>
    public static ICodec<T?> Optional<T, TSentinel>(this ICodec<T> innerCodec,
        ICodec<TSentinel> sentinelCodec, TSentinel absentValue, TSentinel presentValue)
        where T : class
        where TSentinel : IEquatable<TSentinel>
        => new OptionalSentinelCodec<T, TSentinel>(innerCodec, sentinelCodec, absentValue, presentValue);

    /// <summary>
    /// Create a case definition for use in <see cref="Choice{TBase,TTag}"/>.
    /// </summary>
    public static CaseDefinition<TBase> Case<TBase, TCase>(object tag, string label, ICodec<TCase> codec) where TCase : TBase
        => new CaseDefinition<TBase>(tag, label, typeof(TCase), new CaseHandler<TBase, TCase>(codec));

    /// <summary>
    /// Tag-based discriminated union codec. Decode reads the tag,
    /// dispatches to the matching case codec; encode pattern-matches the value type.
    /// </summary>
    public static ICodec<TBase> Choice<TBase, TTag>(ICodec<TTag> tagCodec, params CaseDefinition<TBase>[] cases) where TTag : notnull
        => new ChoiceCodec<TBase, TTag>(tagCodec, cases);
    /// <summary>
    /// Creates a <see cref="DependentFieldFactory{TDep,TField}"/> that produces a
    /// <c>Repeat(count)</c> codec where the count comes from a parent field.
    /// Use with <see cref="RecordBuilder{T}.Field{TDep,TField}"/>.
    /// </summary>
    public static DependentFieldFactory<int, IReadOnlyList<T>> RepeatFrom<T>(this ICodec<T> elementCodec, string countFieldName)
        => new(countFieldName, count => elementCodec.Repeat(count));
 }
