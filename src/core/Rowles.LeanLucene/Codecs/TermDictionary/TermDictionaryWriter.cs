using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Codecs.Fst;
namespace Rowles.LeanLucene.Codecs.TermDictionary;

/// <summary>
/// Writes a sorted term dictionary in v2 compact byte-keyed format.
/// Format: [magic][version=2][termCount:int32][offsets: N×int64][keyStarts: (N+1)×int32][keyData: UTF-8].
/// Binary search on raw UTF-8 bytes — no string materialisation at read time.
/// </summary>
internal static class TermDictionaryWriter
{
    internal static void Write(string filePath, List<string> sortedTerms, Dictionary<string, long> postingsOffsets, bool durable = false)
    {
        using var output = new IndexOutput(filePath, durable);
        CodecConstants.WriteHeader(output, CodecConstants.TermDictionaryVersion);
        TermDictionaryFstBuilder.Write(output, sortedTerms, postingsOffsets);
    }
}
