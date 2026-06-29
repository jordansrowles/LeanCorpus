using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Extension methods on <see cref="IBufferWriter{T}"/> that mirror
/// <see cref="System.IO.BinaryWriter"/> Write methods, enabling body buffering
/// without <see cref="System.IO.MemoryStream"/> or <see cref="System.IO.BinaryWriter"/>.
/// </summary>
internal static class BodyWriterExtensions
{
    // ── Fixed-size primitives ──────────────────────────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteByte(this IBufferWriter<byte> writer, byte value)
    {
        Span<byte> dest = writer.GetSpan(1);
        dest[0] = value;
        writer.Advance(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt32(this IBufferWriter<byte> writer, int value)
    {
        Span<byte> dest = writer.GetSpan(sizeof(int));
        Unsafe.WriteUnaligned(ref dest[0], value);
        writer.Advance(sizeof(int));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteInt64(this IBufferWriter<byte> writer, long value)
    {
        Span<byte> dest = writer.GetSpan(sizeof(long));
        Unsafe.WriteUnaligned(ref dest[0], value);
        writer.Advance(sizeof(long));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteSingle(this IBufferWriter<byte> writer, float value)
    {
        Span<byte> dest = writer.GetSpan(sizeof(float));
        Unsafe.WriteUnaligned(ref dest[0], value);
        writer.Advance(sizeof(float));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBytes(this IBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
    {
        Span<byte> dest = writer.GetSpan(bytes.Length);
        bytes.CopyTo(dest);
        writer.Advance(bytes.Length);
    }

    /// <summary>Writes a sub-range of a byte array.</summary>
    public static void WriteBytes(this IBufferWriter<byte> writer, byte[] buffer, int offset, int count)
    {
        WriteBytes(writer, buffer.AsSpan(offset, count));
    }

    /// <summary>
    /// Writes a non-negative integer using variable-length encoding (7-bit chunks),
    /// compatible with <see cref="System.IO.BinaryWriter.Write7BitEncodedInt"/>.
    /// Small values (0–127) consume a single byte.
    /// </summary>
    public static void Write7BitEncodedInt(this IBufferWriter<byte> writer, int value)
    {
        uint v = (uint)value;
        while (v >= 0x80)
        {
            WriteByte(writer, (byte)(v | 0x80));
            v >>= 7;
        }
        WriteByte(writer, (byte)v);
    }

    /// <summary>
    /// Writes a length-prefixed UTF-8 string, compatible with
    /// <see cref="System.IO.BinaryWriter.Write(string)"/>.
    /// </summary>
    public static void WriteString(this IBufferWriter<byte> writer, string value)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(value);
        Write7BitEncodedInt(writer, byteCount);
        int maxBytes = byteCount;
        Span<byte> dest = writer.GetSpan(maxBytes);
        int written = System.Text.Encoding.UTF8.GetBytes(value, dest);
        writer.Advance(written);
    }

    /// <summary>
    /// Writes a char span as raw UTF-8 bytes (no length prefix).
    /// Compatible with <see cref="System.IO.BinaryWriter.Write(char[])"/>
    /// where the char[] holds ASCII/UTF-8 data.
    /// </summary>
    public static void WriteChars(this IBufferWriter<byte> writer, ReadOnlySpan<char> chars)
    {
        int byteCount = System.Text.Encoding.UTF8.GetByteCount(chars);
        Span<byte> dest = writer.GetSpan(byteCount);
        int written = System.Text.Encoding.UTF8.GetBytes(chars, dest);
        writer.Advance(written);
    }
}
