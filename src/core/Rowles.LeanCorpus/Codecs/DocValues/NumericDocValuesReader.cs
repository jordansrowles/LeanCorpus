using System.Collections.Generic;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens packed per-document numeric values from a column-stride .dvn file.</summary>
internal static class NumericDocValuesReader
{
    public static (Dictionary<string, double[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(string filePath)
    {
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);
        if (!FileOpenRetry.FileExists(filePath))
            return (values, presence);

        using var input = new IndexInput(filePath);
        return Read(input);
    }

    internal static (Dictionary<string, double[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
            var values = new Dictionary<string, double[]>(columns.Count, StringComparer.Ordinal);
            var presence = new Dictionary<string, RoaringBitmap?>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, NumericDocValuesColumn column) in columns)
            {
                values.Add(field, column.Materialise());
                presence.Add(field, column.Presence);
            }
            return (values, presence);
        }
    }

    /// <summary>
    /// Parses metadata and retains packed data offsets without expanding the document values.
    /// The caller owns <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, NumericDocValuesColumn> OpenColumns(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var columns = new Dictionary<string, NumericDocValuesColumn>(StringComparer.Ordinal);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Numeric);

        int fieldCount = input.ReadInt32();
        if (fieldCount < 0)
            throw new InvalidDataException("Numeric DocValues field count cannot be negative.");

        long bodyEnd = checked(frame.BodyStart + frame.BodyLength);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = ReadString(input);
            RoaringBitmap? presence = ReadPresence(input, fieldName);
            int documentCount = input.ReadInt32();
            if (documentCount < 0)
                throw new InvalidDataException($"Numeric DocValues field '{fieldName}' has a negative document count.");

            long minimumBits = input.ReadInt64();
            int bitsPerValue = input.ReadByte();
            if ((uint)bitsPerValue > 64)
                throw new InvalidDataException(
                    $"Invalid bits-per-value {bitsPerValue} for numeric DocValues field '{fieldName}'; must be between 0 and 64.");

            long packedByteCount = checked(((long)bitsPerValue * documentCount + 7) / 8);
            long packedDataOffset = input.Position;
            if (packedDataOffset > bodyEnd || packedByteCount > bodyEnd - packedDataOffset)
                throw new InvalidDataException(
                    $"Numeric DocValues field '{fieldName}' does not contain its declared packed values.");

            columns.Add(fieldName, new NumericDocValuesColumn(
                input, documentCount, minimumBits, bitsPerValue, packedDataOffset, presence));
            input.Seek(checked(packedDataOffset + packedByteCount));
        }

        frame.ValidateChecksum();
        return columns;
    }

    internal static List<(string Name, double[] Values, RoaringBitmap? Presence)> EnumerateFields(string filePath)
    {
        var (values, presence) = Read(filePath);
        var result = new List<(string, double[], RoaringBitmap?)>(values.Count);
        foreach ((string field, double[] column) in values)
            result.Add((field, column, presence.GetValueOrDefault(field)));
        return result;
    }

    private static RoaringBitmap? ReadPresence(IndexInput input, string fieldName)
    {
        int presenceByteCount = input.ReadInt32();
        if (presenceByteCount < 0)
            throw new InvalidDataException($"Numeric DocValues field '{fieldName}' has a negative presence length.");
        if (presenceByteCount == 0)
            return null;

        byte[] bitmapBytes = input.ReadBytes(presenceByteCount);
        using var stream = new MemoryStream(bitmapBytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
        return RoaringBitmap.Deserialise(reader);
    }

    private static string ReadString(IndexInput input)
    {
        int length = input.ReadVarInt();
        if (length < 0)
            throw new InvalidDataException("Negative string length in numeric DocValues.");
        return Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}
