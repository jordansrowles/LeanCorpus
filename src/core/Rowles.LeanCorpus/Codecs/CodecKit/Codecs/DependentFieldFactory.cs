using System;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Describes a dependent field whose codec is created dynamically from a previously decoded field value.
/// </summary>
public sealed class DependentFieldFactory<TDep, TOut>
{
    internal string FieldName { get; }
    internal Func<TDep, ICodec<TOut>> Factory { get; }

    internal DependentFieldFactory(string fieldName, Func<TDep, ICodec<TOut>> factory)
    {
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }
}
