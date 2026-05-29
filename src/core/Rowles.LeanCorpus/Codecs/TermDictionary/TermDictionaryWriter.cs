using System.Text;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Store;
namespace Rowles.LeanCorpus.Codecs.TermDictionary;

/// <summary>
/// Writes a sorted term dictionary as a real FST (Daciuk minimal acyclic transducer)
/// behind the standard LeanCorpus codec header. The file layout is:
/// <code>
/// [magic LLN1 int32][version=3 byte][FST blob produced by FstBuilder.Finish()]
/// </code>
/// The FST blob is self-describing (starts with its own <c>FST1</c> magic and contains
/// the root address and key count). Outputs are the postings file offsets supplied by
/// the caller.
/// </summary>
internal static class TermDictionaryWriter
{
    internal static void Write(string filePath, List<string> sortedTerms, Dictionary<string, long> postingsOffsets, bool durable = false)
    {
        using var output = new IndexOutput(filePath, durable);
        CodecConstants.WriteHeader(output, CodecConstants.TermDictionaryVersion);

        // The caller passes terms ordered by StringComparer.Ordinal, but FstBuilder enforces
        // strict UTF-8 byte order which differs on surrogates. Re-encode and sort here.
        int n = sortedTerms.Count;
        if (n == 0)
        {
            var emptyBlob = new FstBuilder().Finish();
            output.WriteBytes(emptyBlob);
            return;
        }

        var encoded = new (byte[] KeyUtf8, long Output)[n];
        for (int i = 0; i < n; i++)
        {
            string term = sortedTerms[i];
            encoded[i] = (Encoding.UTF8.GetBytes(term), postingsOffsets[term]);
        }
        Array.Sort(encoded, (a, b) => a.KeyUtf8.AsSpan().SequenceCompareTo(b.KeyUtf8));

        var builder = new FstBuilder();
        builder.EnsureNodeCapacity(n);
        for (int i = 0; i < n; i++)
            builder.Add(encoded[i].KeyUtf8, encoded[i].Output);

        var blob = builder.Finish();
        output.WriteBytes(blob);
    }

    /// <summary>
    /// Streams pre-sorted (UTF-8 bytes ascending) term/offset pairs into a new v3 dictionary file.
    /// Used by the migrator to upgrade legacy v1/v2 dictionaries without materialising every
    /// term string.
    /// </summary>
    internal static void WriteSorted(string filePath, IEnumerable<(byte[] KeyUtf8, long Output)> sortedPairs, bool durable = false)
    {
        using var output = new IndexOutput(filePath, durable);
        CodecConstants.WriteHeader(output, CodecConstants.TermDictionaryVersion);

        var builder = new FstBuilder();
        foreach (var (key, offset) in sortedPairs)
            builder.Add(key, offset);

        output.WriteBytes(builder.Finish());
    }
}
