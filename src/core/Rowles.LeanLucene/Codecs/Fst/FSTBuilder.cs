using System.Buffers;
using System.Text;
using Rowles.LeanLucene.Store;
namespace Rowles.LeanLucene.Codecs.Fst;

/// <summary>
/// Serialises a sorted term list into a compact byte-keyed v2 dictionary format.
/// Format: [termCount:int32][postingsOffsets: N*int64][keyStarts: (N+1)*int32][keyData: UTF-8 bytes].
/// This format enables O(log N) binary search on raw UTF-8 bytes without string materialisation.
/// Terms are re-sorted in UTF-8 byte order to ensure binary search correctness
/// (string ordinal sort can differ from UTF-8 byte sort for supplementary characters).
/// </summary>
internal static class TermDictionaryFstBuilder
{
    /// <summary>
    /// Writes sorted terms and their postings offsets in v2 format to the given <paramref name="output"/>.
    /// The codec header (magic + version) must already have been written by the caller.
    /// </summary>
    public static void Write(IndexOutput output, List<string> sortedTerms, Dictionary<string, long> postingsOffsets)
    {
        int count = sortedTerms.Count;
        output.WriteInt32(count);

        if (count == 0) return;

        // Encode all terms into a single contiguous byte buffer to avoid per-term allocation.
        // First pass: compute total byte count.
        int totalBytes = 0;
        var termByteStarts = new int[count + 1];
        for (int i = 0; i < count; i++)
        {
            termByteStarts[i] = totalBytes;
            totalBytes += Encoding.UTF8.GetByteCount(sortedTerms[i]);
        }
        termByteStarts[count] = totalBytes;

        // Second pass: encode into a pooled buffer.
        var allBytes = ArrayPool<byte>.Shared.Rent(totalBytes);
        try
        {
            for (int i = 0; i < count; i++)
            {
                int start = termByteStarts[i];
                int len = termByteStarts[i + 1] - start;
                Encoding.UTF8.GetBytes(sortedTerms[i], allBytes.AsSpan(start, len));
            }

            // Build index sorted by UTF-8 byte order (may differ from string ordinal for surrogates).
            // Uses a struct IComparer for devirtualisation and to avoid delegate allocation.
            var sortedIndices = new int[count];
            for (int i = 0; i < count; i++) sortedIndices[i] = i;
            var comparer = new Utf8SpanComparer(allBytes, termByteStarts);
            Array.Sort(sortedIndices, comparer);

            // Compute key data layout in UTF-8 sorted order
            int dataOffset = 0;
            var starts = new int[count + 1];
            for (int i = 0; i < count; i++)
            {
                starts[i] = dataOffset;
                int idx = sortedIndices[i];
                dataOffset += termByteStarts[idx + 1] - termByteStarts[idx];
            }
            starts[count] = dataOffset;

            // Write postings offsets in UTF-8 sorted order
            for (int i = 0; i < count; i++)
                output.WriteInt64(postingsOffsets[sortedTerms[sortedIndices[i]]]);

            // Write key starts
            for (int i = 0; i <= count; i++)
                output.WriteInt32(starts[i]);

            // Write concatenated UTF-8 key data
            for (int i = 0; i < count; i++)
            {
                int idx = sortedIndices[i];
                output.WriteBytes(allBytes.AsSpan(termByteStarts[idx], termByteStarts[idx + 1] - termByteStarts[idx]));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(allBytes);
        }
    }

    /// <summary>
    /// Struct-based comparer that compares term indices by their UTF-8 byte representations.
    /// Avoids delegate allocation and enables devirtualisation by the JIT.
    /// </summary>
    private readonly struct Utf8SpanComparer(byte[] allBytes, int[] termByteStarts) : IComparer<int>
    {
        public int Compare(int a, int b)
        {
            var spanA = allBytes.AsSpan(termByteStarts[a], termByteStarts[a + 1] - termByteStarts[a]);
            var spanB = allBytes.AsSpan(termByteStarts[b], termByteStarts[b + 1] - termByteStarts[b]);
            return spanA.SequenceCompareTo(spanB);
        }
    }
}
