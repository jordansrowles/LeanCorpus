using System;
using System.Collections.Generic;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Provides named access to the decoded field values inside a
/// <see cref="RecordBuilder{T}.Build(System.Func{FieldValues,T})"/> factory delegate.
/// Access values by the name passed to <c>.Field(...)</c> and cast to the expected type.
/// </summary>
/// <remarks>
/// This type enables an open-ended <c>Build</c> overload that scales to any number of fields
/// without requiring typed-arity overloads. For example:
/// <code>
/// .Build(f => new MyRecord(
///     (string)f["name"],
///     (int)f["count"]))
/// </code>
/// </remarks>
public readonly struct FieldValues
{
    private readonly object?[] _values;
    private readonly IReadOnlyDictionary<string, int> _nameToIndex;

    internal FieldValues(object?[] values, IReadOnlyDictionary<string, int> nameToIndex)
    {
        _values = values;
        _nameToIndex = nameToIndex;
    }

    /// <summary>
    /// Gets the decoded value of the field registered with <paramref name="name"/>.
    /// Cast the returned value to the expected field type.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="name"/> does not match any registered non-constant field.
    /// </exception>
    public object? this[string name]
    {
        get
        {
            if (!_nameToIndex.TryGetValue(name, out int idx))
                throw new ArgumentException($"No field named '{name}' was registered in this record.");
            return _values[idx];
        }
    }
}
