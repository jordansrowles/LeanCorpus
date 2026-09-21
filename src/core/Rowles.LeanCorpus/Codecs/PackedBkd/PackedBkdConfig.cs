namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Immutable dimensions and encoding limits for one packed BKD field.</summary>
internal readonly record struct PackedBkdConfig
{
    internal const int MaxDimensions = 16;
    internal const int MaxIndexedDimensions = 8;
    internal const int FixedBytesPerDimension = 4;

    internal PackedBkdConfig(int dimensions, int indexedDimensions, int bytesPerDimension, int maxPointsPerLeaf)
    {
        if (dimensions is < 1 or > MaxDimensions)
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, $"Dimensions must be between 1 and {MaxDimensions}.");
        if (indexedDimensions is < 1 or > MaxIndexedDimensions || indexedDimensions > dimensions)
            throw new ArgumentOutOfRangeException(nameof(indexedDimensions), indexedDimensions, "Indexed dimensions must be positive, at most eight, and no greater than dimensions.");
        if (bytesPerDimension != FixedBytesPerDimension)
            throw new ArgumentOutOfRangeException(nameof(bytesPerDimension), bytesPerDimension, "Packed BKD v1 uses four bytes per dimension.");
        if (maxPointsPerLeaf is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(maxPointsPerLeaf), maxPointsPerLeaf, "The leaf size must be between one and 4096 points.");

        Dimensions = dimensions;
        IndexedDimensions = indexedDimensions;
        BytesPerDimension = bytesPerDimension;
        MaxPointsPerLeaf = maxPointsPerLeaf;
    }

    internal int Dimensions { get; }
    internal int IndexedDimensions { get; }
    internal int BytesPerDimension { get; }
    internal int MaxPointsPerLeaf { get; }
    internal int PackedBytesLength => checked(Dimensions * BytesPerDimension);
    internal int IndexedBytesLength => checked(IndexedDimensions * BytesPerDimension);
    internal int RecordBytes => checked(PackedBytesLength + sizeof(int));

    internal static PackedBkdConfig Geo2D(int maxPointsPerLeaf = 512)
        => new(2, 2, FixedBytesPerDimension, maxPointsPerLeaf);

    internal static PackedBkdConfig SevenDimensional(int maxPointsPerLeaf = 512)
        => new(7, 4, FixedBytesPerDimension, maxPointsPerLeaf);
}
