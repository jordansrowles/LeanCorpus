using Rowles.LeanCorpus.Codecs.CodecKit;
using System.Text;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

internal static class PackedBkdCodecFiles
{
    internal static CodecFileDescriptor Descriptor { get; } =
        CodecCatalog.Default.GetFile("leancorpus.numeric-structures.packed-bkd");
}

internal sealed class PackedBkdFieldNameComparer : IComparer<string>
{
    internal static PackedBkdFieldNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return 0;
        if (left is null) return -1;
        if (right is null) return 1;
        return Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right));
    }
}
