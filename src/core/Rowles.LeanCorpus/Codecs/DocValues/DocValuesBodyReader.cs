using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Reads one DocValues body with every cursor and length constrained to its codec frame.</summary>
internal sealed class DocValuesBodyReader
{
    private readonly IndexInput _input;
    private readonly CodecBodyReadSession _frame;
    private readonly CodecFileDescriptor _descriptor;
    private readonly int? _expectedDocumentCount;
    private readonly long _bodyEnd;

    internal DocValuesBodyReader(
        IndexInput input,
        CodecBodyReadSession frame,
        CodecFileDescriptor descriptor,
        int? expectedDocumentCount)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(descriptor);
        if (expectedDocumentCount < 0)
            throw new ArgumentOutOfRangeException(nameof(expectedDocumentCount));

        _input = input;
        _frame = frame;
        _descriptor = descriptor;
        _expectedDocumentCount = expectedDocumentCount;
        try
        {
            _bodyEnd = checked(frame.BodyStart + frame.BodyLength);
        }
        catch (OverflowException ex)
        {
            throw Corruption("Codec body end overflows the supported input range.", innerException: ex);
        }

        if (input.Position < frame.BodyStart || input.Position > _bodyEnd)
            throw Corruption("Parser position is outside the codec body.");
    }

    internal long Position => _input.Position;

    internal long BodyEnd => _bodyEnd;

    internal int ReadFieldCount()
    {
        int count = ReadCount("field count", fieldName: null);
        if (count > RemainingBytes)
            throw Corruption($"Field count {count} cannot fit in the remaining {RemainingBytes} body bytes.");
        return count;
    }

    internal int ReadDocumentCount(string fieldName)
    {
        int count = ReadCount("document count", fieldName);
        if (_expectedDocumentCount is int expected && count != expected)
            throw Corruption(
                $"Field '{fieldName}' declares {count} documents, but its segment declares {expected}.",
                fieldName);
        return count;
    }

    internal int ReadCount(string description, string? fieldName)
    {
        int count = ReadInt32(description, fieldName);
        if (count < 0)
            throw Corruption($"{Describe(fieldName)} has a negative {description} ({count}).", fieldName);
        return count;
    }

    internal int ReadInt32(string description, string? fieldName)
    {
        EnsureBodyBytes(sizeof(int), description, fieldName);
        return _input.ReadInt32();
    }

    internal long ReadInt64(string description, string? fieldName)
    {
        EnsureBodyBytes(sizeof(long), description, fieldName);
        return _input.ReadInt64();
    }

    internal int ReadByte(string description, string? fieldName)
    {
        EnsureBodyBytes(sizeof(byte), description, fieldName);
        return _input.ReadByte();
    }

    internal string ReadString(string? fieldName)
    {
        int length = ReadVarInt("string byte length", fieldName);
        if (length > CodecOptions.Default.MaxStringBytes)
            throw new CodecFileException(
                CodecFileErrorCode.LimitExceeded,
                $"{Describe(fieldName)} string length {length} exceeds the codec limit {CodecOptions.Default.MaxStringBytes} bytes.",
                formatId: _descriptor.FormatId,
                formatVersion: _frame.FormatVersion,
                byteOffset: _input.Position);

        byte[] bytes = ReadBytes(length, "string bytes", fieldName);
        return Encoding.UTF8.GetString(bytes);
    }

    internal int[] ReadInt32Array(int count, string description, string fieldName, bool includeTerminalValue = false)
    {
        if (count < 0)
            throw Corruption($"{Describe(fieldName)} has a negative {description} ({count}).", fieldName);

        long elementCount = checked((long)count + (includeTerminalValue ? 1L : 0L));
        if (elementCount > Array.MaxLength)
            throw Corruption($"{Describe(fieldName)} {description} count exceeds the maximum supported array length.", fieldName);

        long byteCount;
        try
        {
            byteCount = checked(elementCount * sizeof(int));
        }
        catch (OverflowException ex)
        {
            throw Corruption($"{Describe(fieldName)} {description} byte length overflows.", fieldName, ex);
        }

        EnsureBodyBytes(byteCount, description, fieldName);
        var values = new int[(int)elementCount];
        _input.ReadInt32Array(values, values.Length);
        return values;
    }

    internal string[] ReadStringArray(int count, string description, string fieldName)
    {
        if (count < 0)
            throw Corruption($"{Describe(fieldName)} has a negative {description} ({count}).", fieldName);
        if (count > RemainingBytes)
            throw Corruption(
                $"{Describe(fieldName)} {description} count {count} cannot fit in the remaining {RemainingBytes} body bytes.",
                fieldName);

        var values = new string[count];
        for (int index = 0; index < values.Length; index++)
            values[index] = ReadString(fieldName);
        return values;
    }

    internal byte[]? ReadPresenceBytes(string fieldName)
    {
        int length = ReadCount("presence bitmap byte count", fieldName);
        return length == 0 ? null : ReadBytes(length, "presence bitmap", fieldName);
    }

    internal RoaringBitmap? DecodePresence(byte[]? bytes, string fieldName, int documentCount)
    {
        if (bytes is null)
            return null;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            RoaringBitmap bitmap = RoaringBitmap.Deserialise(reader);
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Presence bitmap has trailing bytes.");

            foreach (int documentId in bitmap)
            {
                if ((uint)documentId >= (uint)documentCount)
                    throw new InvalidDataException(
                        $"Presence bitmap document ID {documentId} is outside the field's {documentCount} documents.");
            }

            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or OverflowException)
        {
            throw Corruption($"{Describe(fieldName)} has an invalid presence bitmap: {ex.Message}", fieldName, ex);
        }
    }

    internal long ReadPackedByteCount(int valueCount, int bitsPerValue, string fieldName)
    {
        if (valueCount < 0 || bitsPerValue < 0)
            throw Corruption($"{Describe(fieldName)} has a negative packed-value count or width.", fieldName);

        try
        {
            return checked(((long)valueCount * bitsPerValue + 7) / 8);
        }
        catch (OverflowException ex)
        {
            throw Corruption($"{Describe(fieldName)} packed byte count overflows.", fieldName, ex);
        }
    }

    internal byte[] ReadBytes(int count, string description, string? fieldName)
    {
        if (count < 0)
            throw Corruption($"{Describe(fieldName)} has a negative {description} length ({count}).", fieldName);
        EnsureBodyBytes(count, description, fieldName);
        return _input.ReadBytes(count);
    }

    internal void EnsureBodyBytes(long byteCount, string description, string? fieldName)
    {
        if (byteCount < 0 || _input.Position > _bodyEnd || byteCount > _bodyEnd - _input.Position)
            throw Corruption(
                $"{Describe(fieldName)} {description} extends beyond the codec body.",
                fieldName);
    }

    internal void Seek(long position, string description, string? fieldName)
    {
        if (position < _frame.BodyStart || position > _bodyEnd)
            throw Corruption($"{Describe(fieldName)} {description} offset is outside the codec body.", fieldName);
        _input.Seek(position);
    }

    internal void ValidateEnd()
    {
        if (_input.Position != _bodyEnd)
            throw Corruption(
                $"Parsed DocValues body ended at byte {_input.Position}, but its frame body ends at byte {_bodyEnd}.");
    }

    internal CodecFileException Corruption(string message, string? fieldName = null, Exception? innerException = null)
        => new(
            CodecFileErrorCode.SemanticValidationFailure,
            fieldName is null ? $"Invalid DocValues file '{_descriptor.FormatId}': {message}" :
                $"Invalid DocValues file '{_descriptor.FormatId}', field '{fieldName}': {message}",
            formatId: _descriptor.FormatId,
            formatVersion: _frame.FormatVersion,
            byteOffset: _input.Position,
            innerException: innerException);

    internal int ReadVarInt(string description, string? fieldName)
    {
        ulong value = 0;
        for (int index = 0; index < 5; index++)
        {
            byte next = (byte)ReadByte(description, fieldName);
            int shift = index * 7;
            if (shift == 28 && (next & 0xf0) != 0)
                throw Corruption($"{Describe(fieldName)} {description} exceeds Int32 range.", fieldName);

            value |= (ulong)(next & 0x7f) << shift;
            if ((next & 0x80) == 0)
            {
                if (value > int.MaxValue)
                    throw Corruption($"{Describe(fieldName)} {description} exceeds Int32 range.", fieldName);
                return (int)value;
            }
        }

        throw Corruption($"{Describe(fieldName)} {description} is malformed.", fieldName);
    }

    private long RemainingBytes => Math.Max(0, _bodyEnd - _input.Position);

    private string Describe(string? fieldName)
        => fieldName is null ? $"DocValues file '{_descriptor.FormatId}'" : $"DocValues field '{fieldName}'";
}
