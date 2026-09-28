using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted DocValues while retaining packed local ordinals.</summary>
internal static class SortedDocValuesReader
{
    public static (Dictionary<string, string[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(
        string filePath,
        int? expectedDocumentCount = null)
    {
        var values = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);
        if (!FileOpenRetry.FileExists(filePath))
            return (values, presence);

        using var input = new IndexInput(filePath);
        return Read(input, expectedDocumentCount);
    }

    internal static (Dictionary<string, string[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var values = new Dictionary<string, string[]>(columns.Count, StringComparer.Ordinal);
            var presence = new Dictionary<string, RoaringBitmap?>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedDocValuesColumn column) in columns)
            {
                values.Add(field, column.Materialise());
                presence.Add(field, column.Presence);
            }
            return (values, presence);
        }
    }

    /// <summary>
    /// Parses and validates term tables and packed ordinal offsets without expanding one string
    /// reference per document. The caller owns <paramref name="input"/> for the returned columns.
    /// </summary>
    internal static Dictionary<string, SortedDocValuesColumn> OpenColumns(
        IndexInput input,
        int? expectedDocumentCount = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Sorted);
        var body = new DocValuesBodyReader(input, frame, DocValuesCodecFiles.Sorted, expectedDocumentCount);
        int fieldCount = body.ReadFieldCount();
        var columns = new Dictionary<string, SortedDocValuesColumn>(StringComparer.Ordinal);

        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = body.ReadString(fieldName: null);
            byte[]? presenceBytes = body.ReadPresenceBytes(fieldName);
            int documentCount = body.ReadDocumentCount(fieldName);
            RoaringBitmap? presence = body.DecodePresence(presenceBytes, fieldName, documentCount);
            int ordinalCount = body.ReadCount("ordinal count", fieldName);
            if (ordinalCount > documentCount)
                throw body.Corruption(
                    $"Ordinal count {ordinalCount} exceeds the field's {documentCount} documents.",
                    fieldName);
            if (documentCount > 0 && ordinalCount == 0 && frame.FormatVersion < 3)
                throw body.Corruption(
                    "A field with documents has no terms in a format version that requires a placeholder term.",
                    fieldName);
            string[] terms = body.ReadStringArray(ordinalCount, "term table", fieldName);

            int bitsPerOrdinal = body.ReadByte("bits-per-ordinal", fieldName);
            if (bitsPerOrdinal > 63)
                throw body.Corruption(
                    $"Bits-per-ordinal {bitsPerOrdinal} exceeds the supported maximum 63.",
                    fieldName);

            long packedByteCount = body.ReadPackedByteCount(documentCount, bitsPerOrdinal, fieldName);
            long packedDataOffset = body.Position;
            body.EnsureBodyBytes(packedByteCount, "packed ordinals", fieldName);
            var column = new SortedDocValuesColumn(
                input, documentCount, terms, bitsPerOrdinal, packedDataOffset, presence);
            for (int documentId = 0; documentId < documentCount; documentId++)
            {
                try
                {
                    int ordinal = column.GetOrdinal(documentId);
                    bool hasValue = presence is null || presence.Contains(documentId);
                    if (hasValue && (uint)ordinal >= (uint)ordinalCount)
                        throw body.Corruption(
                            $"Ordinal {ordinal} is outside the {ordinalCount}-term table.",
                            fieldName);
                }
                catch (InvalidDataException ex)
                {
                    throw body.Corruption(ex.Message, fieldName, ex);
                }
            }

            if (!columns.TryAdd(fieldName, column))
                throw body.Corruption("Field name is duplicated.", fieldName);
            body.Seek(checked(packedDataOffset + packedByteCount), "packed ordinals", fieldName);
        }

        body.ValidateEnd();
        frame.ValidateChecksum();
        return columns;
    }

    internal static Dictionary<string, string[]> ReadTerms(string filePath, int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, string[]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return ReadTerms(input, expectedDocumentCount);
    }

    internal static Dictionary<string, string[]> ReadTerms(IndexInput input, int? expectedDocumentCount = null)
    {
        using (input)
        {
            var columns = OpenColumns(input, expectedDocumentCount);
            var terms = new Dictionary<string, string[]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedDocValuesColumn column) in columns)
                terms.Add(field, column.CopyTerms());
            return terms;
        }
    }

    internal static List<(string Name, string?[] Values)> EnumerateFields(
        string filePath,
        int? expectedDocumentCount = null)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var (values, presence) = Read(filePath, expectedDocumentCount);
        var results = new List<(string, string?[])>(values.Count);
        foreach ((string field, string[] fieldValues) in values)
        {
            string?[] enumerated = fieldValues;
            if (presence.GetValueOrDefault(field) is { } fieldPresence)
            {
                enumerated = new string?[fieldValues.Length];
                for (int docId = 0; docId < fieldValues.Length; docId++)
                {
                    if (fieldPresence.Contains(docId))
                        enumerated[docId] = fieldValues[docId];
                }
            }

            results.Add((field, enumerated));
        }

        return results;
    }
}
