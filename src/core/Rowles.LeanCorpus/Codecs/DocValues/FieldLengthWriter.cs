using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Writes exact per-field per-doc token counts to a <c>.fln</c> file.
/// Layout v2: [Header][FieldCount:int32]([FieldNameLen:int32][FieldNameUTF8][DocCount:int32][VarInt * DocCount])*
/// Uses VarInt encoding: 1 byte for lengths &lt; 128, 2 bytes for &lt; 16384.
/// </summary>
internal static class FieldLengthWriter
{

    internal static void WriteFieldBlock(IBufferWriter<byte> bw, string fieldName, ReadOnlySpan<int> lengths)
    {
        for (int i = 0; i < lengths.Length; i++)
        {
            if (lengths[i] < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(lengths),
                    lengths[i],
                    $"Field '{fieldName}' length at document {i} must be non-negative.");
        }

        int count = lengths.Length;
        var fieldBytes = Encoding.UTF8.GetBytes(fieldName);
        bw.WriteInt32(fieldBytes.Length);
        bw.WriteBytes(fieldBytes);
        bw.WriteInt32(count);

        for (int i = 0; i < count; i++)
            bw.Write7BitEncodedInt(lengths[i]);
    }

    internal static void Write(string filePath, IReadOnlyDictionary<string, int[]> fieldTokenCounts, int docCount = -1, bool durable = false)
    {
        if (docCount < -1)
            throw new ArgumentOutOfRangeException(nameof(docCount), "Document count must be non-negative or -1 to use each field array's length.");

        foreach (var (fieldName, counts) in fieldTokenCounts)
        {
            if (docCount > counts.Length)
                throw new ArgumentException(
                    $"Field '{fieldName}' has {counts.Length} lengths, fewer than the requested document count {docCount}.",
                    nameof(fieldTokenCounts));
        }

        var descriptor = CodecCatalog.Default.GetFile("leancorpus.field-lengths.data");
        CodecFileWriter.WriteAtomically(filePath, descriptor, durable, bodyOutput =>
        {
            bodyOutput.WriteInt32(fieldTokenCounts.Count);
            foreach (var (fieldName, counts) in fieldTokenCounts)
            {
                int count = docCount < 0 ? counts.Length : docCount;
                WriteFieldBlock(bodyOutput, fieldName, counts.AsSpan(0, count));
            }
        });
    }
}
