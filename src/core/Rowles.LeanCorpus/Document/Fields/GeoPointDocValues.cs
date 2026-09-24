using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Document.Fields;

/// <summary>Preserves exact Geo coordinates for packed-query and distance-sort execution.</summary>
internal static class GeoPointDocValues
{
    private const string FieldPrefix = "\0leancorpus.geo-point:";
    internal const int ValueLength = 2 * sizeof(long);

    internal static string GetFieldName(string pointFieldName)
        => FieldPrefix + pointFieldName;

    internal static void Encode(double latitude, double longitude, Span<byte> destination)
    {
        if (destination.Length < ValueLength)
            throw new ArgumentException("The destination must contain at least 16 bytes.", nameof(destination));

        BinaryPrimitives.WriteInt64LittleEndian(destination, BitConverter.DoubleToInt64Bits(longitude));
        BinaryPrimitives.WriteInt64LittleEndian(
            destination[sizeof(long)..],
            BitConverter.DoubleToInt64Bits(latitude));
    }

    internal static bool TryDecode(
        ReadOnlySpan<byte> value,
        out double latitude,
        out double longitude)
    {
        if (value.Length != ValueLength)
        {
            latitude = default;
            longitude = default;
            return false;
        }

        longitude = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(value));
        latitude = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(value[sizeof(long)..]));
        return double.IsFinite(latitude) && latitude is >= -90 and <= 90
            && double.IsFinite(longitude) && longitude is >= -180 and <= 180;
    }
}
