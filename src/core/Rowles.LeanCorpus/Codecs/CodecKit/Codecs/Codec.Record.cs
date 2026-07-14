using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit;

public static partial class Codec
{
    /// <summary>
    /// Creates a new record builder for type <typeparamref name="T"/>.
    /// </summary>
    public static RecordBuilder<T> Record<T>() => new RecordBuilder<T>();

    /// <summary>
    /// Creates a dependent field factory that produces a codec from a previously decoded field value.
    /// </summary>
    public static DependentFieldFactory<TDep, TOut> From<TDep, TOut>(
        string fieldName, Func<TDep, ICodec<TOut>> factory)
        => new DependentFieldFactory<TDep, TOut>(fieldName, factory);
}
