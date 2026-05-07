using System.Text;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Codecs.DocValues;

/// <summary>
/// Quantises float norms to single bytes and writes them to disc.
/// Writes per-field norms for accurate BM25 field-length normalisation.
/// </summary>
internal static class NormsWriter
{
    internal static void Write(string filePath, IReadOnlyDictionary<string, float[]> fieldNorms, int docCount = -1, bool durable = false)
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
        }
    }
}
