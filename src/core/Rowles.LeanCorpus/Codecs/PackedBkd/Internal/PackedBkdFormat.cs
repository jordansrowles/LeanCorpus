using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Shared Packed BKD v1 constants and field-name encoding rules.</summary>
internal static class PackedBkdFormat
{
    internal const uint FieldMagic = 0x3146_4250; // PBF1
    internal const uint FooterMagic = 0x444b_4250; // PBKD
    internal const int FooterLength = sizeof(uint) + sizeof(int) + sizeof(long);
    internal const byte RawValues = 0;
    internal const byte PrefixValues = 1;
    internal const int MaximumFieldNameBytes = 1 << 20;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static int ValidateFieldName(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        if (fieldName.Length == 0)
            throw new ArgumentException("Packed BKD field names must not be empty.", nameof(fieldName));

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(fieldName);
        }
        catch (EncoderFallbackException ex)
        {
            throw new ArgumentException("Packed BKD field names must contain valid UTF-16 for strict UTF-8 encoding.", nameof(fieldName), ex);
        }

        if (byteCount > MaximumFieldNameBytes)
            throw new ArgumentException($"Packed BKD field names must not exceed {MaximumFieldNameBytes} UTF-8 bytes.", nameof(fieldName));
        return byteCount;
    }

    internal static byte[] EncodeFieldName(string fieldName)
    {
        ValidateFieldName(fieldName);
        return StrictUtf8.GetBytes(fieldName);
    }

    internal static void WriteFieldName(CodecBodyOutput output, string fieldName)
    {
        byte[] bytes = EncodeFieldName(fieldName);
        output.WriteVarInt(bytes.Length);
        output.WriteBytes(bytes);
    }

    internal static string ReadFieldName(IndexInput input, long end)
    {
        uint length = ReadVarUInt(input, end);
        if (length > MaximumFieldNameBytes || length > (ulong)Math.Max(0, end - input.Position))
            throw new InvalidDataException("Packed BKD directory field name is too long or truncated.");

        byte[] bytes = new byte[(int)length];
        if (input.Position > end || bytes.Length > end - input.Position)
            throw new InvalidDataException("Packed BKD directory field name exceeds its bounded section.");
        input.ReadBytes(bytes);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new InvalidDataException("Packed BKD directory field name is not valid UTF-8.", ex);
        }
    }

    private static uint ReadVarUInt(IndexInput input, long end)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (input.Position >= end)
                throw new InvalidDataException("Packed BKD directory field name length is truncated.");
            int next = input.ReadByte();
            if (shift == 28 && (next & 0xF0) != 0)
                throw new InvalidDataException("Packed BKD directory field name length overflows UInt32.");
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
                return value;
        }
        throw new InvalidDataException("Packed BKD directory field name length has an invalid varint.");
    }
}
