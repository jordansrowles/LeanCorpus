namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

internal interface IPackedBkdRecordSource
{
    int Count { get; }

    void Read(int index, Span<byte> destination);
}
