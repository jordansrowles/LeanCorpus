using System.Text;
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
        using var output = new IndexOutput(filePath, durable);

        CodecConstants.WriteHeader(output, CodecConstants.NormsVersion);

        output.WriteInt32(fieldNorms.Count);

        foreach (var (fieldName, norms) in fieldNorms)
        {
            int count = docCount >= 0 ? docCount : norms.Length;
            var fieldBytes = Encoding.UTF8.GetBytes(fieldName);
            output.WriteInt32(fieldBytes.Length);
            output.WriteBytes(fieldBytes);

            output.WriteInt32(count);

            for (int i = 0; i < count; i++)
            {
                byte quantised = (byte)Math.Clamp(MathF.Round(norms[i] * 255f), 0f, 255f);
                output.WriteByte(quantised);
            }

            if (sparseFieldBoosts is not null && sparseFieldBoosts.TryGetValue(fieldName, out var sparseBoosts))
            {
                WriteSparseBoosts(output, sparseBoosts, count);
            }
            else if (fieldBoosts is not null && fieldBoosts.TryGetValue(fieldName, out var boosts))
            {
                WriteDenseBoosts(output, boosts, count);
            }
            else
            {
                output.WriteInt32(0);
            }
        }
    }

    private static void WriteDenseBoosts(IndexOutput output, float[] boosts, int count)
    {
        int boostCount = 0;
        for (int i = 0; i < count; i++)
        {
            if (boosts[i] != 1.0f)
                boostCount++;
        }

        output.WriteInt32(boostCount);
        if (boostCount == 0)
            return;

        for (int i = 0; i < count; i++)
        {
            float boost = boosts[i];
            if (boost == 1.0f)
                continue;

            output.WriteInt32(i);
            output.WriteSingle(boost);
        }
    }

    private static void WriteSparseBoosts(IndexOutput output, IReadOnlyDictionary<int, float> boosts, int count)
    {
        int boostCount = 0;
        foreach (var (docId, boost) in boosts)
        {
            if ((uint)docId < (uint)count && boost != 1.0f)
                boostCount++;
        }

        output.WriteInt32(boostCount);
        if (boostCount == 0)
            return;

        foreach (var (docId, boost) in boosts)
        {
            if ((uint)docId >= (uint)count || boost == 1.0f)
                continue;

            output.WriteInt32(docId);
            output.WriteSingle(boost);
        }
    }
}
