namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Shared overflow-safe tree and leaf encoding calculations.</summary>
internal static class PackedBkdTreeMath
{
    internal static int GetLeftLeafCount(int leafCount)
    {
        if (leafCount < 1)
            throw new ArgumentOutOfRangeException(nameof(leafCount));

        int highestPower = 1;
        while (highestPower <= leafCount / 2)
            highestPower *= 2;

        int baseLeft = highestPower / 2;
        int extra = leafCount - highestPower;
        return extra < baseLeft ? baseLeft + extra : highestPower;
    }

    internal static int GetDocumentWidth(int minimumDocument, int maximumDocument)
    {
        if (minimumDocument < 0 || maximumDocument < minimumDocument)
            throw new ArgumentOutOfRangeException(nameof(minimumDocument));

        long range = (long)maximumDocument - minimumDocument;
        if (range == 0) return 0;
        if (range <= byte.MaxValue) return 1;
        if (range <= ushort.MaxValue) return 2;
        if (range <= 0x00ff_ffff) return 3;
        return sizeof(int);
    }

    internal static int GetTreeDepth(int leafCount)
    {
        if (leafCount < 1)
            throw new ArgumentOutOfRangeException(nameof(leafCount));
        int depth = 0;
        while (leafCount > 1)
        {
            leafCount = leafCount / 2 + leafCount % 2;
            depth++;
        }
        return depth;
    }
}
