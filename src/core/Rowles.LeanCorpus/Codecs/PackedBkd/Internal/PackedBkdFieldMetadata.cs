namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Compact immutable offsets and header metadata for one Packed BKD field.</summary>
internal readonly struct PackedBkdFieldMetadata
{
    internal PackedBkdFieldMetadata(
        PackedBkdConfig config,
        long pointCount,
        int documentCount,
        int leafCount,
        long splitDimensionsOffset,
        long splitValuesOffset,
        long leafOffsetsOffset,
        long leafDataOffset,
        long leafDataLength,
        byte[] rootMinimum,
        byte[] rootMaximum,
        long sectionOffset,
        long sectionLength)
    {
        Config = config;
        PointCount = pointCount;
        DocumentCount = documentCount;
        LeafCount = leafCount;
        SplitDimensionsOffset = splitDimensionsOffset;
        SplitValuesOffset = splitValuesOffset;
        LeafOffsetsOffset = leafOffsetsOffset;
        LeafDataOffset = leafDataOffset;
        LeafDataLength = leafDataLength;
        RootMinimum = rootMinimum;
        RootMaximum = rootMaximum;
        SectionOffset = sectionOffset;
        SectionLength = sectionLength;
    }

    internal PackedBkdConfig Config { get; }
    internal long PointCount { get; }
    internal int DocumentCount { get; }
    internal int LeafCount { get; }
    internal int SplitCount => LeafCount - 1;
    internal long SplitDimensionsOffset { get; }
    internal long SplitValuesOffset { get; }
    internal long LeafOffsetsOffset { get; }
    internal long LeafDataOffset { get; }
    internal long LeafDataLength { get; }
    internal ReadOnlyMemory<byte> RootMinimum { get; }
    internal ReadOnlyMemory<byte> RootMaximum { get; }
    internal long SectionOffset { get; }
    internal long SectionLength { get; }
}
