using System.Collections.Generic;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens packed per-document numeric values from a column-stride .dvn file.</summary>
internal static class NumericDocValuesReader
{
    public static (Dictionary<string, double[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(
        string filePath,
        int? expectedDocumentCount = null)
    {
        var values = new Dictionary<string, double[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);
        if (!FileOpenRetry.FileExists(filePath))
            return (values, presence);

        using var input = new IndexInput(filePath);
        return Read(input, expectedDocumentCount);
    }

    internal static (Dictionary<string, double[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
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
    /// Parses and validates metadata while retaining packed data offsets. The caller owns
    /// <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, NumericDocValuesColumn> OpenColumns(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Numeric);
        var body = new DocValuesBodyReader(input, frame, DocValuesCodecFiles.Numeric, expectedDocumentCount);
        int fieldCount = body.ReadFieldCount();
        var columns = new Dictionary<string, NumericDocValuesColumn>(StringComparer.Ordinal);

        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = body.ReadString(fieldName: null);
            byte[]? presenceBytes = body.ReadPresenceBytes(fieldName);
            int documentCount = body.ReadDocumentCount(fieldName);
            RoaringBitmap? presence = body.DecodePresence(presenceBytes, fieldName, documentCount);
            long minimumBits = body.ReadInt64("minimum value", fieldName);
            int bitsPerValue = body.ReadByte("bits-per-value", fieldName);
            if (bitsPerValue > 64)
                throw body.Corruption(
                    $"Bits-per-value {bitsPerValue} must be between 0 and 64.",
                    fieldName);

            long packedByteCount = body.ReadPackedByteCount(documentCount, bitsPerValue, fieldName);
            long packedDataOffset = body.Position;
            body.EnsureBodyBytes(packedByteCount, "packed values", fieldName);
            var column = new NumericDocValuesColumn(
                input, documentCount, minimumBits, bitsPerValue, packedDataOffset, presence);
            if (!columns.TryAdd(fieldName, column))
                throw body.Corruption("Field name is duplicated.", fieldName);

            body.Seek(checked(packedDataOffset + packedByteCount), "packed values", fieldName);
        }

        body.ValidateEnd();
        frame.ValidateChecksum();
        return columns;
    }

    internal static List<(string Name, double[] Values, RoaringBitmap? Presence)> EnumerateFields(
        string filePath,
        int? expectedDocumentCount = null)
    {
        var (values, presence) = Read(filePath, expectedDocumentCount);
        var result = new List<(string, double[], RoaringBitmap?)>(values.Count);
        foreach ((string field, double[] column) in values)
            result.Add((field, column, presence.GetValueOrDefault(field)));
        return result;
    }
}
