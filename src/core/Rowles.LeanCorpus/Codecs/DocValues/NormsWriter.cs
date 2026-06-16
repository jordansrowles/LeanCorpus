using System.Buffers;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.DocValues;

/// <summary>
/// Quantises float norms to single bytes and writes them to disc.
/// Writes per-field norms for accurate BM25 field-length normalisation.
/// </summary>
internal static class NormsWriter
{
    internal static void Write(
        string filePath,
        IReadOnlyDictionary<string, float[]> fieldNorms,
        IReadOnlyDictionary<string, float[]>? fieldBoosts = null,
        int docCount = -1,
        bool durable = false,
        IReadOnlyDictionary<string, Dictionary<int, float>>? sparseFieldBoosts = null)
    {
        var bodyBuf = new ArrayBufferWriter<byte>(4096);

        bodyBuf.WriteInt32(fieldNorms.Count);

        foreach (var (fieldName, norms) in fieldNorms)
        {
            int count = docCount >= 0 ? docCount : norms.Length;
            var fieldBytes = Encoding.UTF8.GetBytes(fieldName);
            bodyBuf.WriteInt32(fieldBytes.Length);
            bodyBuf.WriteBytes(fieldBytes);

            bodyBuf.WriteInt32(count);

            for (int i = 0; i < count; i++)
            {
                byte quantised = (byte)Math.Clamp(MathF.Round(norms[i] * 255f), 0f, 255f);
                bodyBuf.WriteByte(quantised);
            }

            if (sparseFieldBoosts is not null && sparseFieldBoosts.TryGetValue(fieldName, out var sparseBoosts))
            {
                WriteSparseBoosts(bodyBuf, sparseBoosts, count);
            }
            else if (fieldBoosts is not null && fieldBoosts.TryGetValue(fieldName, out var boosts))
            {
                WriteDenseBoosts(bodyBuf, boosts, count);
            }
            else
            {
                bodyBuf.WriteInt32(0);
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

        bw.WriteInt32(boostCount);
        if (boostCount == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            float boost = boosts[i];
            if (boost == 1.0f)
                continue;

            bw.WriteInt32(i);
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

        bw.WriteInt32(boostCount);
        if (boostCount == 0)
            return;

        foreach (var (docId, boost) in boosts)
        {
            if ((uint)docId >= (uint)count || boost == 1.0f)
                continue;

            bw.WriteInt32(docId);
            bw.WriteSingle(boost);
        }
    }
}