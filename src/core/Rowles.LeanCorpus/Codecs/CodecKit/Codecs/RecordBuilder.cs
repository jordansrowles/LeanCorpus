using System;
using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

/// <summary>
/// Fluent builder for composing multiple field codecs into a single record codec.
/// Fields are decoded/encoded in registration order.
/// </summary>
public sealed class RecordBuilder<T>
{
    private readonly List<BuilderFieldEntry> _fields = new();
    private readonly HashSet<string> _fieldNames = new HashSet<string>(StringComparer.Ordinal);

    internal RecordBuilder() { }

    /// <summary>
    /// Adds a regular field.
    /// </summary>
    public RecordBuilder<T> Field<TField>(string name, Func<T, TField> getter, ICodec<TField> codec)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (getter == null) throw new ArgumentNullException(nameof(getter));
        if (codec == null) throw new ArgumentNullException(nameof(codec));

        ValidateUniqueName(name);

        var def = new RegularFieldDefinition<T, TField>(name, getter, codec);
        _fields.Add(new BuilderFieldEntry(def, def, def, getter: getter, outputType: typeof(TField)));
        return this;
    }

    /// <summary>
    /// Adds a dependent field whose codec is produced by a factory keyed on a previously decoded field.
    /// </summary>
    public RecordBuilder<T> Field<TDep, TField>(string name, Func<T, TField> getter,
        DependentFieldFactory<TDep, TField> dependent)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (getter == null) throw new ArgumentNullException(nameof(getter));
        if (dependent == null) throw new ArgumentNullException(nameof(dependent));

        ValidateUniqueName(name);

        string depFieldName = dependent.FieldName;
        int depIndex = FindFieldIndex(depFieldName, name);
        var depEntry = _fields[depIndex];

        // Validate the dependency field type
        ValidateDependencyType<TDep>(depEntry, depFieldName, name);

        // Extract the dependency getter
        var depGetter = ExtractGetter<TDep>(depEntry);

        var def = new DependentFieldDefinition<T, TDep, TField>(
            name, getter, dependent.Factory, depIndex, depGetter);
        _fields.Add(new BuilderFieldEntry(def, def, def, getter: getter, outputType: typeof(TField)));
        return this;
    }

    /// <summary>
    /// Adds a constant field (e.g. magic, padding). Not passed to Build factory.
    /// </summary>
    public RecordBuilder<T> Constant(string name, ICodec<Unit> codec)
    {
        if (name == null) throw new ArgumentNullException(nameof(name));
        if (codec == null) throw new ArgumentNullException(nameof(codec));

        ValidateUniqueName(name);

        var def = new ConstantFieldDefinition<T>(name, codec);
        _fields.Add(new BuilderFieldEntry(def, def, def));
        return this;
    }

    private void ValidateUniqueName(string name)
    {
        if (!_fieldNames.Add(name))
            throw new CodecValidationException(CodecErrorCode.DuplicateFieldName, 0, string.Empty,
                $"Duplicate field name: '{name}'.");
    }

    private int FindFieldIndex(string depFieldName, string currentFieldName)
    {
        for (int i = 0; i < _fields.Count; i++)
        {
            if (string.Equals(_fields[i].Definition.Name, depFieldName, StringComparison.Ordinal))
                return i;
        }
        throw new ArgumentException(
            $"Dependent field '{currentFieldName}' references non-existent field '{depFieldName}'.");
    }

    private static void ValidateDependencyType<TDep>(BuilderFieldEntry entry, string depFieldName, string currentFieldName)
    {
        // Check if the dependency field produces a value of type TDep
        var depType = GetFieldOutputType(entry);
        if (depType != null && depType != typeof(TDep))
        {
            throw new ArgumentException(
                $"Dependent field '{currentFieldName}' expects dependency '{depFieldName}' " +
                $"to produce {typeof(TDep).Name}, but it produces {depType.Name}.");
        }
        if (entry.Definition.IsConstant)
        {
            throw new ArgumentException(
                $"Dependent field '{currentFieldName}' references constant field '{depFieldName}', which produces no value.");
        }
    }

    private static Type? GetFieldOutputType(BuilderFieldEntry entry)
    {
        return entry.OutputType;
    }

    private Func<T, TDep> ExtractGetter<TDep>(BuilderFieldEntry entry)
    {
        if (entry.Getter is Func<T, TDep> typedGetter)
            return typedGetter;

        throw new InvalidOperationException(
            $"Failed to extract getter for dependency field. This is a bug in RecordBuilder.");
    }

    private RecordCodec<T>.FieldEntry[] BuildFieldEntries()
    {
        var entries = new RecordCodec<T>.FieldEntry[_fields.Count];
        for (int i = 0; i < _fields.Count; i++)
        {
            entries[i] = new RecordCodec<T>.FieldEntry(
                _fields[i].Definition, _fields[i].Decoder, _fields[i].Encoder);
        }
        return entries;
    }

    private int CountNonConstant()
    {
        int count = 0;
        for (int i = 0; i < _fields.Count; i++)
            if (!_fields[i].Definition.IsConstant) count++;
        return count;
    }

    private int[] BuildArgToField()
    {
        int[] mapping = new int[CountNonConstant()];
        int arg = 0;
        for (int i = 0; i < _fields.Count; i++)
            if (!_fields[i].Definition.IsConstant)
                mapping[arg++] = i;
        return mapping;
    }

    private void ValidateArity(int expected)
    {
        int actual = CountNonConstant();
        if (actual != expected)
            throw new ArgumentException(
                $"Build factory expects {expected} parameter(s), but {actual} non-constant field(s) were defined.");
    }

    // --- Build overloads for arities 1-16 ---

    public ICodec<T> Build<T1>(Func<T1, T> factory)
    {
        ValidateArity(1);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory((T1)args[0]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2>(Func<T1, T2, T> factory)
    {
        ValidateArity(2);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3>(Func<T1, T2, T3, T> factory)
    {
        ValidateArity(3);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4>(Func<T1, T2, T3, T4, T> factory)
    {
        ValidateArity(4);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5>(Func<T1, T2, T3, T4, T5, T> factory)
    {
        ValidateArity(5);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6>(Func<T1, T2, T3, T4, T5, T6, T> factory)
    {
        ValidateArity(6);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7>(Func<T1, T2, T3, T4, T5, T6, T7, T> factory)
    {
        ValidateArity(7);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T> factory)
    {
        ValidateArity(8);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T> factory)
    {
        ValidateArity(9);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T> factory)
    {
        ValidateArity(10);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!, (T10)args[9]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T> factory)
    {
        ValidateArity(11);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!, (T10)args[9]!, (T11)args[10]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T> factory)
    {
        ValidateArity(12);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!, (T10)args[9]!, (T11)args[10]!, (T12)args[11]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T> factory)
    {
        ValidateArity(13);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!, (T10)args[9]!, (T11)args[10]!, (T12)args[11]!, (T13)args[12]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T> factory)
    {
        ValidateArity(14);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!, (T10)args[9]!, (T11)args[10]!, (T12)args[11]!, (T13)args[12]!, (T14)args[13]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T> factory)
    {
        ValidateArity(15);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!, (T10)args[9]!, (T11)args[10]!, (T12)args[11]!, (T13)args[12]!, (T14)args[13]!, (T15)args[14]!), BuildArgToField());
    }

    public ICodec<T> Build<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(Func<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T> factory)
    {
        ValidateArity(16);
        return new RecordCodec<T>(BuildFieldEntries(), args =>
            factory(
            (T1)args[0]!, (T2)args[1]!, (T3)args[2]!, (T4)args[3]!, (T5)args[4]!, (T6)args[5]!, (T7)args[6]!, (T8)args[7]!, (T9)args[8]!, (T10)args[9]!, (T11)args[10]!, (T12)args[11]!, (T13)args[12]!, (T14)args[13]!, (T15)args[14]!, (T16)args[15]!), BuildArgToField());
    }

    /// <summary>
    /// Builds a record codec using a <see cref="FieldValues"/> accessor, supporting any number of fields.
    /// Access decoded values by name — <c>f["fieldName"]</c> — and cast to the expected type.
    /// </summary>
    public ICodec<T> Build(Func<FieldValues, T> factory)
    {
        if (factory == null) throw new ArgumentNullException(nameof(factory));
        var fieldEntries = BuildFieldEntries();
        var nameToIndex = BuildNameToIndexMap();
        return new RecordCodec<T>(fieldEntries, args =>
            factory(new FieldValues(args, nameToIndex)), BuildArgToField());
    }

    private IReadOnlyDictionary<string, int> BuildNameToIndexMap()
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        int idx = 0;
        for (int i = 0; i < _fields.Count; i++)
        {
            if (!_fields[i].Definition.IsConstant)
                map[_fields[i].Definition.Name] = idx++;
        }
        return map;
    }

    internal sealed class BuilderFieldEntry
    {
        public readonly IFieldDefinition Definition;
        public readonly IFieldDecoder Decoder;
        public readonly IFieldEncoder<T> Encoder;
        public readonly Delegate? Getter;
        public readonly Type? OutputType;

        public BuilderFieldEntry(IFieldDefinition definition, IFieldDecoder decoder, IFieldEncoder<T> encoder,
            Delegate? getter = null, Type? outputType = null)
        {
            Definition = definition;
            Decoder = decoder;
            Encoder = encoder;
            Getter = getter;
            OutputType = outputType;
        }
    }
}
