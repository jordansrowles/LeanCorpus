using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Quantises float norms to single bytes and writes them to disc.
/// Writes per-field norms for accurate BM25 field-length normalisation.
/// </summary>
internal static class NormsWriter
{
    /// <summary>Quantises a float norm in [0,1] to a single byte, matching the on-disc format.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static byte QuantiseNorm(float norm)
        => (byte)Math.Clamp(MathF.Round(norm * 255f), 0f, 255f);


    internal static void WriteFieldBlock(
        IBufferWriter<byte> bw,
        string fieldName,
        float[] values,
        float[]? boosts,
        int docCount)
    {
        var fieldBytes = Encoding.UTF8.GetBytes(fieldName);
        PostingsWriter.WriteVarInt(bw, fieldBytes.Length);
        bw.WriteBytes(fieldBytes);

        PostingsWriter.WriteVarInt(bw, docCount);

        for (int i = 0; i < docCount; i++)
            bw.WriteByte(QuantiseNorm(values[i]));

        if (boosts is not null)
        {
            WriteDenseBoosts(bw, boosts, docCount);
        }
        else
        {
            PostingsWriter.WriteVarInt(bw, 0);
        }
    }

    internal static void Write(
        string filePath,
        IReadOnlyDictionary<string, float[]> fieldNorms,
        IReadOnlyDictionary<string, float[]>? fieldBoosts = null,
        int docCount = -1,
        bool durable = false,
        IReadOnlyDictionary<string, Dictionary<int, float>>? sparseFieldBoosts = null)
    {
        var bodyBuf = new ArrayBufferWriter<byte>(4096);

        PostingsWriter.WriteVarInt(bodyBuf, fieldNorms.Count);

        foreach (var (fieldName, norms) in fieldNorms)
        {
            int count = docCount >= 0 ? docCount : norms.Length;

            if (sparseFieldBoosts is not null && sparseFieldBoosts.TryGetValue(fieldName, out var sparseBoosts))
            {
                // Sparse boosts handled inline; write header + norms then sparse boosts
                var fieldBytes = Encoding.UTF8.GetBytes(fieldName);
                PostingsWriter.WriteVarInt(bodyBuf, fieldBytes.Length);
                bodyBuf.WriteBytes(fieldBytes);

                PostingsWriter.WriteVarInt(bodyBuf, count);

                for (int i = 0; i < count; i++)
                    bodyBuf.WriteByte(QuantiseNorm(norms[i]));

                WriteSparseBoosts(bodyBuf, sparseBoosts, count);
            }
            else
            {
                float[]? boosts = fieldBoosts is not null && fieldBoosts.TryGetValue(fieldName, out var dense) ? dense : null;
                WriteFieldBlock(bodyBuf, fieldName, norms, boosts, count);
            }
        }

        using var output = new IndexOutput(filePath, durable);
        CodecFileHeader.Write(output, CodecFormats.Norms, bodyBuf.WrittenSpan);
    }

    private static void WriteDenseBoosts(IBufferWriter<byte> bw, float[] boosts, int count)
    {
        int boostCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (boosts[i] != 1.0f)
                boostCount++;
        }

        PostingsWriter.WriteVarInt(bw, boostCount);
        if (boostCount == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            float boost = boosts[i];
            if (boost == 1.0f)
                continue;

            PostingsWriter.WriteVarInt(bw, i);
            bw.WriteSingle(boost);
        }
    }

    private static void WriteSparseBoosts(IBufferWriter<byte> bw, IReadOnlyDictionary<int, float> boosts, int count)
    {
        int boostCount = 0;
        foreach (var (docId, boost) in boosts)
        {
            if ((uint)docId < (uint)count && boost != 1.0f)
                boostCount++;
        }

        PostingsWriter.WriteVarInt(bw, boostCount);
        if (boostCount == 0)
            return;

        foreach (var (docId, boost) in boosts)
        {
            if ((uint)docId >= (uint)count || boost == 1.0f)
                continue;

            PostingsWriter.WriteVarInt(bw, docId);
            bw.WriteSingle(boost);
        }
    }
}