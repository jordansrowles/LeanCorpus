using System.Buffers.Binary;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

/// <summary>Encodes and validates the stable 28-byte Packed BKD shape primitive.</summary>
internal static class ShapePrimitiveCodec
{
    internal const int PackedValueLength = 28;
    internal const uint MaximumValueOrdinal = 0x03FF_FFFF;
    private const int BytesPerDimension = 4;
    private const int MetadataDimension = 6;
    private const uint EdgeFlagsMask = 0x38;

    private enum ReconstructionCode : uint
    {
        Code0 = 0,
        Code1 = 1,
        Code2 = 2,
        Code3 = 3,
        Code4 = 4,
        Code5 = 5,
        Code6 = 6,
        Code7 = 7,
    }

    internal static void EncodePoint(
        Span<byte> destination,
        SpatialFieldKind fieldKind,
        ShapeVertex point,
        uint valueOrdinal)
    {
        ValidateOrdinal(valueOrdinal);
        EncodeCore(
            destination,
            fieldKind,
            point, true,
            point, true,
            point, true,
            valueOrdinal,
            allowDegenerate: true);
    }

    internal static void EncodeLine(
        Span<byte> destination,
        SpatialFieldKind fieldKind,
        ShapeVertex first,
        ShapeVertex second,
        uint valueOrdinal)
    {
        ValidateOrdinal(valueOrdinal);
        EncodedVertex a = Prepare(first, fieldKind);
        EncodedVertex b = Prepare(second, fieldKind);
        if (Same(a, b))
        {
            EncodePoint(destination, fieldKind, first, valueOrdinal);
            return;
        }

        if (Compare(a, b) > 0)
            (a, b) = (b, a);

        EncodeCore(
            destination,
            fieldKind,
            a.ToVertex(fieldKind), true,
            b.ToVertex(fieldKind), true,
            a.ToVertex(fieldKind), true,
            valueOrdinal,
            allowDegenerate: true);
    }

    internal static void EncodeTriangle(
        Span<byte> destination,
        SpatialFieldKind fieldKind,
        ShapeVertex a,
        bool edgeAB,
        ShapeVertex b,
        bool edgeBC,
        ShapeVertex c,
        bool edgeCA,
        uint valueOrdinal)
    {
        ValidateOrdinal(valueOrdinal);
        EncodeCore(
            destination,
            fieldKind,
            a, edgeAB,
            b, edgeBC,
            c, edgeCA,
            valueOrdinal,
            allowDegenerate: false);
    }

    internal static ShapePrimitive Decode(ReadOnlySpan<byte> source, SpatialFieldKind fieldKind)
    {
        if (source.Length != PackedValueLength)
            throw new InvalidDataException($"A shape primitive must contain exactly {PackedValueLength} bytes.");
        ValidateShapeKind(fieldKind);

        uint metadata = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(MetadataDimension * BytesPerDimension, BytesPerDimension));
        uint code = metadata & 0x7;
        uint edgeBits = metadata & EdgeFlagsMask;
        uint valueOrdinal = metadata >> 6;

        EncodedVertex a;
        EncodedVertex b;
        EncodedVertex c;
        switch ((ReconstructionCode)code)
        {
            case ReconstructionCode.Code0:
                a = ReadVertex(source, 0, 1, fieldKind);
                b = ReadVertex(source, 2, 3, fieldKind);
                c = ReadVertex(source, 4, 5, fieldKind);
                break;
            case ReconstructionCode.Code1:
                a = ReadVertex(source, 0, 1, fieldKind);
                b = ReadVertex(source, 4, 5, fieldKind);
                c = ReadVertex(source, 2, 3, fieldKind);
                break;
            case ReconstructionCode.Code2:
                a = ReadVertex(source, 2, 1, fieldKind);
                b = ReadVertex(source, 4, 5, fieldKind);
                c = ReadVertex(source, 0, 3, fieldKind);
                break;
            case ReconstructionCode.Code3:
                a = ReadVertex(source, 2, 1, fieldKind);
                b = ReadVertex(source, 0, 3, fieldKind);
                c = ReadVertex(source, 4, 5, fieldKind);
                break;
            case ReconstructionCode.Code4:
                a = ReadVertex(source, 4, 1, fieldKind);
                b = ReadVertex(source, 0, 5, fieldKind);
                c = ReadVertex(source, 2, 3, fieldKind);
                break;
            case ReconstructionCode.Code5:
                a = ReadVertex(source, 4, 1, fieldKind);
                b = ReadVertex(source, 0, 3, fieldKind);
                c = ReadVertex(source, 2, 5, fieldKind);
                break;
            case ReconstructionCode.Code6:
                a = ReadVertex(source, 2, 1, fieldKind);
                b = ReadVertex(source, 0, 5, fieldKind);
                c = ReadVertex(source, 4, 3, fieldKind);
                break;
            case ReconstructionCode.Code7:
                a = ReadVertex(source, 0, 1, fieldKind);
                b = ReadVertex(source, 4, 3, fieldKind);
                c = ReadVertex(source, 2, 5, fieldKind);
                break;
            default:
                throw new InvalidDataException("The shape reconstruction code is invalid.");
        }

        uint minY = Math.Min(a.YKey, Math.Min(b.YKey, c.YKey));
        uint minX = Math.Min(a.XKey, Math.Min(b.XKey, c.XKey));
        uint maxY = Math.Max(a.YKey, Math.Max(b.YKey, c.YKey));
        uint maxX = Math.Max(a.XKey, Math.Max(b.XKey, c.XKey));
        if (minY != ReadDimensionKey(source, 0)
            || minX != ReadDimensionKey(source, 1)
            || maxY != ReadDimensionKey(source, 2)
            || maxX != ReadDimensionKey(source, 3))
            throw new InvalidDataException("The reconstructed shape bounds do not match the indexed bounds.");

        bool edgeAB = (edgeBits & (1u << 3)) != 0;
        bool edgeBC = (edgeBits & (1u << 4)) != 0;
        bool edgeCA = (edgeBits & (1u << 5)) != 0;
        bool sameAB = Same(a, b);
        bool sameBC = Same(b, c);
        bool sameCA = Same(c, a);
        ShapePrimitiveKind kind;
        if (sameAB && sameBC)
        {
            kind = ShapePrimitiveKind.Point;
            if (!edgeAB || !edgeBC || !edgeCA)
                throw new InvalidDataException("Point primitive source-edge flags are not canonical.");
        }
        else if (sameCA && !sameAB && !sameBC)
        {
            kind = ShapePrimitiveKind.Line;
            if (!edgeAB || !edgeBC || !edgeCA || Compare(a, b) >= 0)
                throw new InvalidDataException("Line primitive degeneracy or source-edge flags are not canonical.");
        }
        else if (sameAB || sameBC || sameCA || SignedArea(a, b, c) <= 0)
        {
            throw new InvalidDataException("A shape triangle must have three distinct counter-clockwise vertices.");
        }
        else
        {
            kind = ShapePrimitiveKind.Triangle;
            if (!IsCanonicalFirst(a, b, c))
                throw new InvalidDataException("The shape triangle does not use the canonical first vertex.");
        }

        if ((uint)FindReconstruction(a, b, c) != code)
            throw new InvalidDataException("The shape reconstruction code is not canonical for its vertices.");

        return new ShapePrimitive(
            a.ToVertex(fieldKind), b.ToVertex(fieldKind), c.ToVertex(fieldKind),
            edgeAB, edgeBC, edgeCA,
            valueOrdinal, kind);
    }

    private static void EncodeCore(
        Span<byte> destination,
        SpatialFieldKind fieldKind,
        ShapeVertex inputA,
        bool inputAB,
        ShapeVertex inputB,
        bool inputBC,
        ShapeVertex inputC,
        bool inputCA,
        uint valueOrdinal,
        bool allowDegenerate)
    {
        ValidateOrdinal(valueOrdinal);
        if (destination.Length < PackedValueLength)
            throw new ArgumentException($"The destination needs {PackedValueLength} bytes.", nameof(destination));
        ValidateShapeKind(fieldKind);

        EncodedVertex a = Prepare(inputA, fieldKind);
        EncodedVertex b = Prepare(inputB, fieldKind);
        EncodedVertex c = Prepare(inputC, fieldKind);
        bool edgeAB = inputAB;
        bool edgeBC = inputBC;
        bool edgeCA = inputCA;

        RotateToReferenceFirst(ref a, ref b, ref c, ref edgeAB, ref edgeBC, ref edgeCA);
        double area = SignedArea(a, b, c);
        if (area < 0)
        {
            (b, c) = (c, b);
            (edgeAB, edgeCA) = (edgeCA, edgeAB);
        }

        bool sameAB = Same(a, b);
        bool sameBC = Same(b, c);
        bool sameCA = Same(c, a);
        if (!allowDegenerate && (sameAB || sameBC || sameCA || SignedArea(a, b, c) == 0))
            throw new ArgumentException("A shape triangle must have three distinct non-collinear vertices.");
        if (allowDegenerate && !(sameAB && sameBC) && !(sameCA && !sameAB && !sameBC))
            throw new ArgumentException("A degenerate shape primitive must be a point or canonical A,B,A line.");
        if (allowDegenerate && (!edgeAB || !edgeBC || !edgeCA))
            throw new ArgumentException("Point and line primitives must mark all three edges as source boundaries.");

        ReconstructionCode code = FindReconstruction(a, b, c);
        uint minY = Math.Min(a.YKey, Math.Min(b.YKey, c.YKey));
        uint minX = Math.Min(a.XKey, Math.Min(b.XKey, c.XKey));
        uint maxY = Math.Max(a.YKey, Math.Max(b.YKey, c.YKey));
        uint maxX = Math.Max(a.XKey, Math.Max(b.XKey, c.XKey));
        uint extraY;
        uint extraX;
        switch (code)
        {
            case ReconstructionCode.Code0:
                extraY = c.YKey;
                extraX = c.XKey;
                break;
            case ReconstructionCode.Code1:
                extraY = b.YKey;
                extraX = b.XKey;
                break;
            case ReconstructionCode.Code2:
                extraY = b.YKey;
                extraX = b.XKey;
                break;
            case ReconstructionCode.Code3:
                extraY = c.YKey;
                extraX = c.XKey;
                break;
            case ReconstructionCode.Code4:
                extraY = a.YKey;
                extraX = b.XKey;
                break;
            case ReconstructionCode.Code5:
                extraY = a.YKey;
                extraX = c.XKey;
                break;
            case ReconstructionCode.Code6:
                extraY = c.YKey;
                extraX = b.XKey;
                break;
            default:
                extraY = b.YKey;
                extraX = c.XKey;
                break;
        }

        WriteDimensionKey(destination, 0, minY);
        WriteDimensionKey(destination, 1, minX);
        WriteDimensionKey(destination, 2, maxY);
        WriteDimensionKey(destination, 3, maxX);
        WriteDimensionKey(destination, 4, extraY);
        WriteDimensionKey(destination, 5, extraX);
        uint metadata = (valueOrdinal << 6)
            | ((uint)code & 0x7)
            | (edgeAB ? 1u << 3 : 0)
            | (edgeBC ? 1u << 4 : 0)
            | (edgeCA ? 1u << 5 : 0);
        BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(MetadataDimension * BytesPerDimension, BytesPerDimension), metadata);
    }

    private static ReconstructionCode FindReconstruction(EncodedVertex a, EncodedVertex b, EncodedVertex c)
    {
        uint minY = Math.Min(a.YKey, Math.Min(b.YKey, c.YKey));
        uint maxY = Math.Max(a.YKey, Math.Max(b.YKey, c.YKey));
        uint maxX = Math.Max(a.XKey, Math.Max(b.XKey, c.XKey));
        if (minY == a.YKey)
        {
            if (maxY == b.YKey && maxX == b.XKey)
                return ReconstructionCode.Code0;
            if (maxY == c.YKey && maxX == c.XKey)
                return ReconstructionCode.Code1;
            return ReconstructionCode.Code7;
        }

        if (maxY == a.YKey)
        {
            if (minY == b.YKey && maxX == b.XKey)
                return ReconstructionCode.Code3;
            if (minY == c.YKey && maxX == c.XKey)
                return ReconstructionCode.Code2;
            return ReconstructionCode.Code6;
        }

        if (maxX == b.XKey && minY == b.YKey)
            return ReconstructionCode.Code5;
        if (maxX == c.XKey && maxY == c.YKey)
            return ReconstructionCode.Code4;
        throw new ArgumentException("The shape triangle cannot be encoded canonically.");
    }

    private static void RotateToReferenceFirst(
        ref EncodedVertex a,
        ref EncodedVertex b,
        ref EncodedVertex c,
        ref bool edgeAB,
        ref bool edgeBC,
        ref bool edgeCA)
    {
        if (Compare(b, a) < 0 && Compare(b, c) <= 0)
        {
            (a, b, c) = (b, c, a);
            (edgeAB, edgeBC, edgeCA) = (edgeBC, edgeCA, edgeAB);
        }
        else if (Compare(c, a) < 0 && Compare(c, b) < 0)
        {
            (a, b, c) = (c, a, b);
            (edgeAB, edgeBC, edgeCA) = (edgeCA, edgeAB, edgeBC);
        }
    }

    private static bool IsCanonicalFirst(EncodedVertex a, EncodedVertex b, EncodedVertex c)
        => Compare(a, b) <= 0 && Compare(a, c) <= 0;

    private static EncodedVertex Prepare(ShapeVertex vertex, SpatialFieldKind fieldKind)
    {
        if (vertex.HasPreparedKeys)
        {
            if (vertex.PreparedKind != fieldKind)
                throw new ArgumentException("A prepared shape coordinate belongs to a different spatial field kind.", nameof(vertex));
            return new EncodedVertex(vertex.XKey, vertex.YKey, vertex.X, vertex.Y);
        }

        if (!double.IsFinite(vertex.X) || !double.IsFinite(vertex.Y))
            throw new ArgumentOutOfRangeException(nameof(vertex), "Shape coordinates must be finite.");

        if (fieldKind == SpatialFieldKind.GeoShape)
        {
            int x = GeoEncodingUtils.EncodeLon(vertex.X);
            int y = GeoEncodingUtils.EncodeLat(vertex.Y);
            return new EncodedVertex(
                unchecked((uint)(x ^ int.MinValue)),
                unchecked((uint)(y ^ int.MinValue)),
                GeoEncodingUtils.DecodeLon(x),
                GeoEncodingUtils.DecodeLat(y));
        }

        float xValue = (float)vertex.X;
        float yValue = (float)vertex.Y;
        if (!float.IsFinite(xValue) || !float.IsFinite(yValue))
            throw new ArgumentOutOfRangeException(nameof(vertex), "XY shape coordinates must be finite floats.");
        if (xValue == 0) xValue = 0;
        if (yValue == 0) yValue = 0;
        uint xKey = SortableCoordinateEncoding.SortableFloatBits(xValue);
        uint yKey = SortableCoordinateEncoding.SortableFloatBits(yValue);
        return new EncodedVertex(
            xKey,
            yKey,
            SortableCoordinateEncoding.UnsortableFloatBits(xKey),
            SortableCoordinateEncoding.UnsortableFloatBits(yKey));
    }

    private static EncodedVertex ReadVertex(ReadOnlySpan<byte> source, int yDimension, int xDimension, SpatialFieldKind fieldKind)
    {
        uint xKey = ReadDimensionKey(source, xDimension);
        uint yKey = ReadDimensionKey(source, yDimension);
        if (fieldKind == SpatialFieldKind.GeoShape)
        {
            int x = unchecked((int)(xKey ^ 0x8000_0000u));
            int y = unchecked((int)(yKey ^ 0x8000_0000u));
            return new EncodedVertex(xKey, yKey, GeoEncodingUtils.DecodeLon(x), GeoEncodingUtils.DecodeLat(y));
        }

        float xValue = SortableCoordinateEncoding.UnsortableFloatBits(xKey);
        float yValue = SortableCoordinateEncoding.UnsortableFloatBits(yKey);
        if (!float.IsFinite(xValue) || !float.IsFinite(yValue))
            throw new InvalidDataException("A shape primitive contains a non-finite XY coordinate.");
        return new EncodedVertex(xKey, yKey, xValue, yValue);
    }

    private static void ValidateShapeKind(SpatialFieldKind fieldKind)
    {
        if (fieldKind is not (SpatialFieldKind.GeoShape or SpatialFieldKind.XYShape))
            throw new InvalidDataException($"Spatial field kind '{fieldKind}' cannot contain shape primitives.");
    }

    internal static ShapeVertex Quantise(ShapeVertex vertex, SpatialFieldKind fieldKind)
    {
        ValidateShapeKind(fieldKind);
        EncodedVertex encoded = Prepare(vertex, fieldKind);
        return encoded.ToVertex(fieldKind);
    }

    internal static double DecodeXKey(uint key, SpatialFieldKind fieldKind)
    {
        ValidateShapeKind(fieldKind);
        return fieldKind == SpatialFieldKind.GeoShape
            ? GeoEncodingUtils.DecodeLon(unchecked((int)(key ^ 0x8000_0000u)))
            : SortableCoordinateEncoding.UnsortableFloatBits(key);
    }

    internal static double DecodeYKey(uint key, SpatialFieldKind fieldKind)
    {
        ValidateShapeKind(fieldKind);
        return fieldKind == SpatialFieldKind.GeoShape
            ? GeoEncodingUtils.DecodeLat(unchecked((int)(key ^ 0x8000_0000u)))
            : SortableCoordinateEncoding.UnsortableFloatBits(key);
    }

    private static void ValidateOrdinal(uint valueOrdinal)
    {
        if (valueOrdinal > MaximumValueOrdinal)
            throw new ArgumentOutOfRangeException(nameof(valueOrdinal), valueOrdinal, "A shape field value ordinal must fit in 26 bits.");
    }

    private static double SignedArea(EncodedVertex a, EncodedVertex b, EncodedVertex c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    private static bool Same(EncodedVertex a, EncodedVertex b)
        => a.XKey == b.XKey && a.YKey == b.YKey;

    private static int Compare(EncodedVertex a, EncodedVertex b)
    {
        int x = a.XKey.CompareTo(b.XKey);
        return x != 0 ? x : a.YKey.CompareTo(b.YKey);
    }

    private static uint ReadDimensionKey(ReadOnlySpan<byte> source, int dimension)
        => BinaryPrimitives.ReadUInt32BigEndian(source.Slice(dimension * BytesPerDimension, BytesPerDimension));

    private static void WriteDimensionKey(Span<byte> destination, int dimension, uint key)
        => BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(dimension * BytesPerDimension, BytesPerDimension), key);

    private readonly record struct EncodedVertex(uint XKey, uint YKey, double X, double Y)
    {
        internal ShapeVertex ToVertex(SpatialFieldKind fieldKind) => new(X, Y, XKey, YKey, fieldKind);
    }
}
