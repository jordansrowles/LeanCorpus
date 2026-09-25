using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Internal;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal static class ShapeDocValuesMetadata
{
    internal static ShapeRecordMetadata Compute(
        SpatialFieldKind fieldKind,
        uint valueCount,
        ReadOnlyMemory<byte> primitiveBytes)
    {
        if (valueCount == 0 || primitiveBytes.Length == 0
            || primitiveBytes.Length % ShapePrimitiveCodec.PackedValueLength != 0)
            throw new InvalidDataException("Shape DocValues metadata requires values and complete packed primitives.");

        bool geo = fieldKind == SpatialFieldKind.GeoShape;
        bool xy = fieldKind == SpatialFieldKind.XYShape;
        if (!geo && !xy)
            throw new InvalidDataException($"Spatial field kind '{fieldKind}' cannot contain Shape DocValues.");

        int primitiveCount = primitiveBytes.Length / ShapePrimitiveCodec.PackedValueLength;
        uint minY = uint.MaxValue;
        uint maxY = uint.MinValue;
        uint minX = uint.MaxValue;
        uint maxX = uint.MinValue;
        var longitudeIntervals = geo ? new List<LongitudeInterval>(primitiveCount) : null;
        SpatialDimension highestDimension = SpatialDimension.Point;
        double accumulator0 = 0;
        double accumulator1 = 0;
        double accumulator2 = 0;
        double weight = 0;
        ReadOnlySpan<byte> bytes = primitiveBytes.Span;

        for (int i = 0; i < primitiveCount; i++)
        {
            ReadOnlySpan<byte> encoded = bytes.Slice(i * ShapePrimitiveCodec.PackedValueLength, ShapePrimitiveCodec.PackedValueLength);
            ShapePrimitive primitive = ShapePrimitiveCodec.Decode(encoded, fieldKind);
            uint d0 = BinaryPrimitives.ReadUInt32BigEndian(encoded);
            uint d1 = BinaryPrimitives.ReadUInt32BigEndian(encoded[4..]);
            uint d2 = BinaryPrimitives.ReadUInt32BigEndian(encoded[8..]);
            uint d3 = BinaryPrimitives.ReadUInt32BigEndian(encoded[12..]);
            minY = Math.Min(minY, d0);
            maxY = Math.Max(maxY, d2);
            minX = Math.Min(minX, d1);
            maxX = Math.Max(maxX, d3);
            if (geo)
                longitudeIntervals!.Add(new LongitudeInterval(
                    ShapePrimitiveCodec.DecodeXKey(d1, fieldKind),
                    ShapePrimitiveCodec.DecodeXKey(d3, fieldKind)));

            SpatialDimension dimension = primitive.Kind switch
            {
                ShapePrimitiveKind.Point => SpatialDimension.Point,
                ShapePrimitiveKind.Line => SpatialDimension.Line,
                ShapePrimitiveKind.Triangle => SpatialDimension.Area,
                _ => throw new InvalidDataException("A Shape DocValues primitive kind is invalid."),
            };
            if (dimension > highestDimension)
            {
                highestDimension = dimension;
                accumulator0 = 0;
                accumulator1 = 0;
                accumulator2 = 0;
                weight = 0;
            }

            if (dimension != highestDimension)
                continue;

            double x;
            double y;
            double primitiveWeight;
            switch (dimension)
            {
                case SpatialDimension.Point:
                    x = primitive.A.X;
                    y = primitive.A.Y;
                    primitiveWeight = 1;
                    break;
                case SpatialDimension.Line:
                    double deltaX = primitive.B.X - primitive.A.X;
                    double deltaY = primitive.B.Y - primitive.A.Y;
                    primitiveWeight = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                    if (primitiveWeight == 0)
                        continue;
                    x = (primitive.A.X + primitive.B.X) / 2;
                    y = (primitive.A.Y + primitive.B.Y) / 2;
                    break;
                case SpatialDimension.Area:
                    double cross = ((primitive.B.X - primitive.A.X) * (primitive.C.Y - primitive.A.Y))
                        - ((primitive.B.Y - primitive.A.Y) * (primitive.C.X - primitive.A.X));
                    primitiveWeight = Math.Abs(cross) / 2;
                    if (primitiveWeight == 0)
                        continue;
                    x = (primitive.A.X + primitive.B.X + primitive.C.X) / 3;
                    y = (primitive.A.Y + primitive.B.Y + primitive.C.Y) / 3;
                    break;
                default:
                    throw new InvalidDataException("Shape DocValues spatial dimension is invalid.");
            }

            if (geo)
            {
                accumulator0 += y * primitiveWeight;
                double radians = x * (Math.PI / 180d);
                accumulator1 += Math.Sin(radians) * primitiveWeight;
                accumulator2 += Math.Cos(radians) * primitiveWeight;
            }
            else
            {
                accumulator0 += x * primitiveWeight;
                accumulator1 += y * primitiveWeight;
            }
            weight += primitiveWeight;
        }

        if (!double.IsFinite(accumulator0) || !double.IsFinite(accumulator1)
            || !double.IsFinite(accumulator2) || !double.IsFinite(weight) || weight <= 0)
            throw new InvalidDataException("Shape DocValues centroid accumulators are non-finite or have non-positive weight.");

        var bounds = new byte[24];
        byte flags = 0;
        if (geo)
        {
            (double west, double east, bool wraps) = MinimalLongitudeEnvelope(longitudeIntervals!);
            flags = wraps ? (byte)1 : (byte)0;
            WriteGeoLatitude(bounds, 0, ShapePrimitiveCodec.DecodeYKey(minY, fieldKind));
            WriteGeoLatitude(bounds, 1, ShapePrimitiveCodec.DecodeYKey(maxY, fieldKind));
            WriteGeoLongitude(bounds, 2, west);
            WriteGeoLongitude(bounds, 3, east);
            WriteGeoLongitude(bounds, 4, ShapePrimitiveCodec.DecodeXKey(minX, fieldKind));
            WriteGeoLongitude(bounds, 5, ShapePrimitiveCodec.DecodeXKey(maxX, fieldKind));
        }
        else
        {
            WriteRawCode(bounds, 0, minX);
            WriteRawCode(bounds, 1, maxX);
            WriteRawCode(bounds, 2, minY);
            WriteRawCode(bounds, 3, maxY);
            // The last two XY bound slots are reserved zero values.
        }

        return new ShapeRecordMetadata(
            highestDimension,
            flags,
            bounds,
            accumulator0,
            accumulator1,
            accumulator2,
            weight);
    }

    private static (double West, double East, bool Wraps) MinimalLongitudeEnvelope(List<LongitudeInterval> intervals)
    {
        if (intervals.Count == 0)
            throw new InvalidDataException("A Geo shape record has no longitude intervals.");

        intervals.Sort(static (left, right) =>
        {
            int comparison = left.West.CompareTo(right.West);
            return comparison != 0 ? comparison : left.East.CompareTo(right.East);
        });

        var merged = new List<LongitudeInterval>(intervals.Count);
        foreach (LongitudeInterval interval in intervals)
        {
            double start = interval.West + 180d;
            double end = interval.East + 180d;
            if (merged.Count == 0 || start > merged[^1].East)
                merged.Add(new LongitudeInterval(start, end));
            else if (end > merged[^1].East)
                merged[^1] = merged[^1] with { East = end };
        }

        double largestGap = double.NegativeInfinity;
        double westCoordinate = 0;
        double eastCoordinate = 0;
        for (int i = 0; i < merged.Count; i++)
        {
            LongitudeInterval current = merged[i];
            LongitudeInterval next = merged[(i + 1) % merged.Count];
            double nextStart = i + 1 == merged.Count ? next.West + 360d : next.West;
            double gap = nextStart - current.East;
            double candidateWest = next.West;
            double candidateEast = current.East;
            if (gap > largestGap || (gap == largestGap && candidateWest < westCoordinate))
            {
                largestGap = gap;
                westCoordinate = candidateWest;
                eastCoordinate = candidateEast;
            }
        }

        double west = NormaliseLongitude(westCoordinate - 180d);
        double east = NormaliseLongitude(eastCoordinate - 180d);
        return (west, east, west > east);
    }

    private static double NormaliseLongitude(double longitude)
    {
        while (longitude < -180d)
            longitude += 360d;
        while (longitude > 180d)
            longitude -= 360d;
        return longitude == 0 ? 0 : longitude;
    }

    private static void WriteGeoLatitude(Span<byte> destination, int slot, double coordinate)
    {
        int encoded = GeoEncodingUtils.EncodeLat(coordinate);
        uint sortable = unchecked((uint)(encoded ^ int.MinValue));
        WriteRawCode(destination, slot, sortable);
    }

    private static void WriteGeoLongitude(Span<byte> destination, int slot, double coordinate)
    {
        int encoded = GeoEncodingUtils.EncodeLon(coordinate);
        uint sortable = unchecked((uint)(encoded ^ int.MinValue));
        WriteRawCode(destination, slot, sortable);
    }

    private static void WriteRawCode(Span<byte> destination, int slot, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(slot * sizeof(uint), sizeof(uint)), value);

    private readonly record struct LongitudeInterval(double West, double East);
}
