using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>Opens sorted DocValues while retaining packed local ordinals.</summary>
internal static class SortedDocValuesReader
{
    public static (Dictionary<string, string[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(string filePath)
    {
        var values = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var presence = new Dictionary<string, RoaringBitmap?>(StringComparer.Ordinal);
        if (!FileOpenRetry.FileExists(filePath))
            return (values, presence);

        using var input = new IndexInput(filePath);
        return Read(input);
    }

    internal static (Dictionary<string, string[]> Values, Dictionary<string, RoaringBitmap?> Presence) Read(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
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
    /// Parses term tables and packed ordinal offsets without expanding one string reference per document.
    /// The caller owns <paramref name="input"/> for the lifetime of the returned columns.
    /// </summary>
    internal static Dictionary<string, SortedDocValuesColumn> OpenColumns(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var columns = new Dictionary<string, SortedDocValuesColumn>(StringComparer.Ordinal);
        using var frame = CodecFileReader.OpenSupported(input, DocValuesCodecFiles.Sorted);

        int fieldCount = input.ReadInt32();
        if (fieldCount < 0)
            throw new InvalidDataException("Sorted DocValues field count cannot be negative.");

        long bodyEnd = checked(frame.BodyStart + frame.BodyLength);
        for (int fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            string fieldName = ReadString(input);
            RoaringBitmap? presence = ReadPresence(input, fieldName);
            int documentCount = input.ReadInt32();
            int ordinalCount = input.ReadInt32();
            if (documentCount < 0 || ordinalCount < 0)
                throw new InvalidDataException($"Sorted DocValues field '{fieldName}' has a negative count.");

            var terms = new string[ordinalCount];
            for (int ordinal = 0; ordinal < terms.Length; ordinal++)
                terms[ordinal] = ReadString(input);

            int bitsPerOrdinal = input.ReadByte();
            if (bitsPerOrdinal > 63)
                throw new InvalidDataException(
                    $"Sorted DocValues field '{fieldName}' has bitsPerOrd={bitsPerOrdinal}, max is 63.");

            long packedByteCount = checked(((long)documentCount * bitsPerOrdinal + 7) / 8);
            long packedDataOffset = input.Position;
            if (packedDataOffset > bodyEnd || packedByteCount > bodyEnd - packedDataOffset)
                throw new InvalidDataException(
                    $"Sorted DocValues field '{fieldName}' does not contain its declared packed ordinals.");

            var column = new SortedDocValuesColumn(
                input, documentCount, terms, bitsPerOrdinal, packedDataOffset, presence);
            for (int documentId = 0; documentId < documentCount; documentId++)
            {
                int ordinal = column.GetOrdinal(documentId);
                if ((uint)ordinal >= (uint)ordinalCount)
                    throw new InvalidDataException(
                        $"Sorted DocValues field '{fieldName}' has ordinal {ordinal} but ordTable has {ordinalCount} entries.");
            }

            columns.Add(fieldName, column);
            input.Seek(checked(packedDataOffset + packedByteCount));
        }

        frame.ValidateChecksum();
        return columns;
    }

    internal static Dictionary<string, string[]> ReadTerms(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return new Dictionary<string, string[]>(StringComparer.Ordinal);

        using var input = new IndexInput(filePath);
        return ReadTerms(input);
    }

    internal static Dictionary<string, string[]> ReadTerms(IndexInput input)
    {
        using (input)
        {
            var columns = OpenColumns(input);
            var terms = new Dictionary<string, string[]>(columns.Count, StringComparer.Ordinal);
            foreach ((string field, SortedDocValuesColumn column) in columns)
                terms.Add(field, column.CopyTerms());
            return terms;
        }
    }

    internal static List<(string Name, string?[] Values)> EnumerateFields(string filePath)
    {
        if (!FileOpenRetry.FileExists(filePath))
            return [];

        var (values, presence) = Read(filePath);
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

    private static RoaringBitmap? ReadPresence(IndexInput input, string fieldName)
    {
        int presenceByteCount = input.ReadInt32();
        if (presenceByteCount < 0)
            throw new InvalidDataException($"Sorted DocValues field '{fieldName}' has a negative presence length.");
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
            throw new InvalidDataException("Negative string length in sorted DocValues.");
        return Encoding.UTF8.GetString(input.ReadBytes(length));
    }
}
