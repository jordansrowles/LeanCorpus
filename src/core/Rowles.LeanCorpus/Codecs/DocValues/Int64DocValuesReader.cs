using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens packed per-document Int64 values from a column-stride .dvnl file.</summary>
internal static class Int64DocValuesReader
{
    public static (Dictionary<string, long[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(
        string filePath,
        int? expectedDocumentCount = null)
    {
        var values = new Dictionary<string, long[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);
        if (!FileOpenRetry.FileExists(filePath))
            return (values, presence);

        using var input = new IndexInput(filePath);
        return Read(input, expectedDocumentCount);
    }

    internal static (Dictionary<string, long[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var values = new Dictionary<string, long[]>(columns.Count, StringComparer.Ordinal);
            var presence = new Dictionary<string, RoaringBitmap?>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, Int64DocValuesColumn column) in columns)
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
    internal static Dictionary<string, Int64DocValuesColumn> OpenColumns(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Int64);
        var body = new DocValuesBodyReader(input, frame, DocValuesCodecFiles.Int64, expectedDocumentCount);
        int fieldCount = body.ReadFieldCount();
        var columns = new Dictionary<string, Int64DocValuesColumn>(StringComparer.Ordinal);

        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = body.ReadString(fieldName: null);
            byte[]? presenceBytes = body.ReadPresenceBytes(fieldName);
            int documentCount = body.ReadDocumentCount(fieldName);
            RoaringBitmap? presence = body.DecodePresence(presenceBytes, fieldName, documentCount);
            long minimum = body.ReadInt64("minimum value", fieldName);
            int bitsPerValue = body.ReadByte("bits-per-value", fieldName);
            if (bitsPerValue > 64)
                throw body.Corruption(
                    $"Bits-per-value {bitsPerValue} must be between 0 and 64.",
                    fieldName);

            long packedByteCount = body.ReadPackedByteCount(documentCount, bitsPerValue, fieldName);
            long packedDataOffset = body.Position;
            body.EnsureBodyBytes(packedByteCount, "packed values", fieldName);
            var column = new Int64DocValuesColumn(
                input, documentCount, minimum, bitsPerValue, packedDataOffset, presence);
            if (!columns.TryAdd(fieldName, column))
                throw body.Corruption("Field name is duplicated.", fieldName);
            body.Seek(checked(packedDataOffset + packedByteCount), "packed values", fieldName);
        }

        body.ValidateEnd();
        frame.ValidateChecksum();
        return columns;
    }
}
