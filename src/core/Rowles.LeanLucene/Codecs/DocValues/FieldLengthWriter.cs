using System.Text;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Writes exact per-field per-doc token counts to a <c>.fln</c> file.
/// Layout v2: [Header][FieldCount:int32]([FieldNameLen:int32][FieldNameUTF8][DocCount:int32][VarInt * DocCount])*
/// Uses VarInt encoding: 1 byte for lengths &lt; 128, 2 bytes for &lt; 16384.
/// </summary>
internal static class FieldLengthWriter
{
    internal static void Write(string filePath, IReadOnlyDictionary<string, int[]> fieldTokenCounts, int docCount = -1, bool durable = false)
    {
        using var output = new IndexOutput(filePath, durable);

        CodecConstants.WriteHeader(output, CodecConstants.FieldLengthVersion);
        output.WriteInt32(fieldTokenCounts.Count);

        foreach (var (fieldName, counts) in fieldTokenCounts)
        {
            int count = docCount >= 0 ? docCount : counts.Length;
            var fieldBytes = Encoding.UTF8.GetBytes(fieldName);
            output.WriteInt32(fieldBytes.Length);
            output.WriteBytes(fieldBytes);
            output.WriteInt32(count);

            for (int i = 0; i < count; i++)
            {
                int val = Math.Clamp(counts[i], 0, ushort.MaxValue);
                output.WriteVarInt(val);
            }
        }
    }
}
