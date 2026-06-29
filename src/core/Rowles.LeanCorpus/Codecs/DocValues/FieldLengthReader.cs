using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Reads exact per-field per-doc token counts from a <c>.fln</c> file.
/// Returns <c>Dictionary&lt;string, int[]&gt;</c> keyed by field name.
/// Uses VarInt encoding.
/// Falls back gracefully when the file does not exist (caller should use quantised norms).
/// </summary>
internal static class FieldLengthReader
{
    /// <summary>
    /// Tries to load exact field lengths. Returns null if the file does not exist.
    /// Throws on corrupt/invalid data.
    /// </summary>
    public static Dictionary<string, int[]>? TryRead(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        using var input = new IndexInput(filePath);
        byte version = CodecFileHeader.ReadVersion(input, CodecFormats.FieldLengths);

        int fieldCount = input.ReadInt32();
        var result = new Dictionary<string, int[]>(fieldCount, StringComparer.Ordinal);

        for (int f = 0; f < fieldCount; f++)
        {
            int nameLen = input.ReadInt32();
            var nameBytes = new byte[nameLen];
            for (int b = 0; b < nameLen; b++)
                nameBytes[b] = input.ReadByte();
            string fieldName = Encoding.UTF8.GetString(nameBytes);

            int docCount = input.ReadInt32();
            var lengths = new int[docCount];

            // Current format: VarInt encoding
            for (int d = 0; d < docCount; d++)
                lengths[d] = input.ReadVarInt();

            result[fieldName] = lengths;
        }

        return result;
    }
}
