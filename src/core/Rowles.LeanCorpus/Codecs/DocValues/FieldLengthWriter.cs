using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Writes exact per-field per-doc token counts to a <c>.fln</c> file.
/// Layout v2: [Header][FieldCount:int32]([FieldNameLen:int32][FieldNameUTF8][DocCount:int32][VarInt * DocCount])*
/// Uses VarInt encoding: 1 byte for lengths &lt; 128, 2 bytes for &lt; 16384.
/// </summary>
internal static class FieldLengthWriter
{

    internal static void WriteFieldBlock(IBufferWriter<byte> bw, string fieldName, int[] lengths)
    {
        int count = lengths.Length;
        var fieldBytes = Encoding.UTF8.GetBytes(fieldName);
        bw.WriteInt32(fieldBytes.Length);
        bw.WriteBytes(fieldBytes);
        bw.WriteInt32(count);

        for (int i = 0; i < count; i++)
        {
            int val = Math.Clamp(lengths[i], 0, ushort.MaxValue);
            bw.Write7BitEncodedInt(val);
        }
    }

    internal static void Write(string filePath, IReadOnlyDictionary<string, int[]> fieldTokenCounts, int docCount = -1, bool durable = false)
    {
        var bodyBuf = new ArrayBufferWriter<byte>(4096);

        bodyBuf.WriteInt32(fieldTokenCounts.Count);

        foreach (var (fieldName, counts) in fieldTokenCounts)
        {
            WriteFieldBlock(bodyBuf, fieldName, counts);
        }

        using var output = new IndexOutput(filePath, durable);
        CodecFileHeader.Write(output, CodecFormats.FieldLengths, bodyBuf.WrittenSpan);
    }
}
