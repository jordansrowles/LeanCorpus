using Rowles.LeanCorpus.Search;

namespace Rowles.LeanCorpus.Search.XY;

/// <summary>Encodes finite Cartesian floats into unsigned lexicographically sortable bytes.</summary>
public static class XYEncodingUtils
{
    /// <summary>Writes one sortable four-byte x coordinate.</summary>
    public static void Encode(float value, Span<byte> destination)
    {
        SortableCoordinateEncoding.WriteSortableFloat(value, destination);
    }

    /// <summary>Decodes one sortable four-byte coordinate.</summary>
    public static float Decode(ReadOnlySpan<byte> source)
    {
        return SortableCoordinateEncoding.ReadSortableFloat(source);
    }

}
