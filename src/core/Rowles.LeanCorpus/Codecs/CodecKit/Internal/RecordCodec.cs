using System;
using System.Buffers;
using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;

namespace Rowles.LeanCorpus.Codecs.CodecKit.Internal;

/// <summary>
/// Internal interfaces for record field encode/decode.
/// </summary>
internal interface IFieldEncoder<T>
{
    void Encode(T value, IBufferWriter<byte> writer, CodecContext context);
}

internal interface IFieldDecoder
{
    object? Decode(ref SequenceReader<byte> reader, CodecContext context, object?[] previousFields);
}

internal interface IFieldDefinition
{
    string Name { get; }
    bool IsConstant { get; }
}

/// <summary>
/// Composes N field codecs into a single ICodec&lt;T&gt;.
/// Decode reads all fields in order, then calls the build delegate.
/// Encode calls each getter and writes each field in order.
/// </summary>
internal sealed class RecordCodec<T> : ICodec<T>
{
    private readonly FieldEntry[] _fields;
    private readonly Func<object?[], T> _buildFromArray;
    private readonly int _nonConstantCount;
    private readonly int[] _argToField;  // arg position → field index for non-constant fields

    public RecordCodec(FieldEntry[] fields, Func<object?[], T> buildFromArray, int[] argToField)
    {
        _fields = fields;
        _buildFromArray = buildFromArray;
        _nonConstantCount = argToField.Length;
        _argToField = argToField;
    }

    public T Decode(ref SequenceReader<byte> reader, CodecContext context)
    {
        var checkpoint = context.Checkpoint(ref reader);
        var allValues = new object?[_fields.Length];

        try
        {
            using var depthGuard = context.PushDepth();
            for (int i = 0; i < _fields.Length; i++)
            {
                var field = _fields[i];
                using var pathGuard = context.PushPath(">" + field.Definition.Name);
                allValues[i] = field.Decoder.Decode(ref reader, context, allValues);
            }
        }
        catch
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }

        // Build from non-constant fields using the precomputed remap
        var buildArgs = new object?[_nonConstantCount];
        for (int i = 0; i < _nonConstantCount; i++)
            buildArgs[i] = allValues[_argToField[i]];

        try
        {
            return _buildFromArray(buildArgs);
        }
        catch (CodecException)
        {
            context.Rewind(ref reader, checkpoint);
            throw;
        }
        catch (Exception ex)
        {
            context.Rewind(ref reader, checkpoint);
            throw new UserCodeException(context.GetByteOffset(ref reader), context.CurrentPath, ex);
        }
    }

    public void Encode(T value, IBufferWriter<byte> writer, CodecContext context)
    {
        using var depthGuard = context.PushDepth();
        for (int i = 0; i < _fields.Length; i++)
        {
            var field = _fields[i];
            field.Encoder.Encode(value, writer, context);
        }
    }

    internal sealed class FieldEntry
    {
        public readonly IFieldDefinition Definition;
        public readonly IFieldDecoder Decoder;
        public readonly IFieldEncoder<T> Encoder;

        public FieldEntry(IFieldDefinition definition, IFieldDecoder decoder, IFieldEncoder<T> encoder)
        {
            Definition = definition;
            Decoder = decoder;
            Encoder = encoder;
        }
    }
}

/// <summary>A regular (non-dependent, non-constant) field.</summary>
internal sealed class RegularFieldDefinition<T, TField> : IFieldDefinition, IFieldDecoder, IFieldEncoder<T>
{
    private readonly ICodec<TField> _codec;
    private readonly Func<T, TField> _getter;

    public string Name { get; }
    public bool IsConstant => false;

    public RegularFieldDefinition(string name, Func<T, TField> getter, ICodec<TField> codec)
    {
        Name = name;
        _getter = getter;
        _codec = codec;
    }

    public object? Decode(ref SequenceReader<byte> reader, CodecContext context, object?[] previousFields)
    {
        return _codec.Decode(ref reader, context);
    }

    public void Encode(T value, IBufferWriter<byte> writer, CodecContext context)
    {
        TField fieldValue;
        try
        {
            fieldValue = _getter(value);
        }
        catch (CodecException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserCodeException(0, context.CurrentPath, ex);
        }
        _codec.Encode(fieldValue, writer, context);
    }
}

/// <summary>A constant field (e.g. magic bytes, padding). Not passed to Build delegate.</summary>
internal sealed class ConstantFieldDefinition<T> : IFieldDefinition, IFieldDecoder, IFieldEncoder<T>
{
    private readonly ICodec<Unit> _codec;

    public string Name { get; }
    public bool IsConstant => true;

    public ConstantFieldDefinition(string name, ICodec<Unit> codec)
    {
        Name = name;
        _codec = codec;
    }

    public object? Decode(ref SequenceReader<byte> reader, CodecContext context, object?[] previousFields)
    {
        _codec.Decode(ref reader, context);
        return null;
    }

    public void Encode(T value, IBufferWriter<byte> writer, CodecContext context)
    {
        _codec.Encode(Unit.Value, writer, context);
    }
}

/// <summary>A dependent field whose codec is created from a previously decoded field value.</summary>
internal sealed class DependentFieldDefinition<T, TDep, TField> : IFieldDefinition, IFieldDecoder, IFieldEncoder<T>
{
    private readonly Func<TDep, ICodec<TField>> _factory;
    private readonly Func<T, TField> _getter;
    private readonly int _dependencyIndex;
    private readonly Func<T, TDep> _depGetter;

    public string Name { get; }
    public bool IsConstant => false;

    public DependentFieldDefinition(string name, Func<T, TField> getter, Func<TDep, ICodec<TField>> factory,
        int dependencyIndex, Func<T, TDep> depGetter)
    {
        Name = name;
        _getter = getter;
        _factory = factory;
        _dependencyIndex = dependencyIndex;
        _depGetter = depGetter;
    }

    public object? Decode(ref SequenceReader<byte> reader, CodecContext context, object?[] previousFields)
    {
        var depValue = (TDep)previousFields[_dependencyIndex]!;
        ICodec<TField> codec;
        try
        {
            codec = _factory(depValue);
        }
        catch (CodecException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserCodeException(0, context.CurrentPath, ex);
        }
        return codec.Decode(ref reader, context);
    }

    public void Encode(T value, IBufferWriter<byte> writer, CodecContext context)
    {
        TDep depValue;
        TField fieldValue;
        try
        {
            depValue = _depGetter(value);
            fieldValue = _getter(value);
        }
        catch (CodecException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserCodeException(0, context.CurrentPath, ex);
        }

        ICodec<TField> codec;
        try
        {
            codec = _factory(depValue);
        }
        catch (CodecException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserCodeException(0, context.CurrentPath, ex);
        }

        codec.Encode(fieldValue, writer, context);
    }
}
