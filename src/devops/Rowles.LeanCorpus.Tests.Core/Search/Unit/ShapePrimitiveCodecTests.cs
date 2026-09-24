using System.Buffers.Binary;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class ShapePrimitiveCodecTests
{
    [Theory(DisplayName = "Shape primitive reconstruction codes have stable golden bytes")]
    [InlineData(0, 0d, 0d, 10d, 10d, 5d, 7d, "8000000080000000c1200000c1200000c0e00000c0a0000000000000")]
    [InlineData(1, 0d, 0d, 5d, 3d, 10d, 10d, "8000000080000000c1200000c1200000c0400000c0a0000000000001")]
    [InlineData(2, 0d, 10d, 5d, 4d, 10d, 0d, "8000000080000000c1200000c1200000c0800000c0a0000000000002")]
    [InlineData(3, 0d, 10d, 10d, 0d, 5d, 7d, "8000000080000000c1200000c1200000c0e00000c0a0000000000003")]
    [InlineData(4, 0d, 5d, 5d, 0d, 10d, 10d, "8000000080000000c1200000c1200000c0a00000c0a0000000000004")]
    [InlineData(5, 0d, 5d, 10d, 0d, 5d, 10d, "8000000080000000c1200000c1200000c0a00000c0a0000000000005")]
    [InlineData(6, 0d, 10d, 4d, 0d, 10d, 5d, "8000000080000000c1200000c1200000c0a00000c080000000000006")]
    [InlineData(7, 0d, 0d, 10d, 4d, 5d, 10d, "8000000080000000c1200000c1200000c0800000c0a0000000000007")]
    public void EncodeTriangle_UsesGoldenBytesForEveryReconstructionCode(
        int expectedCode,
        double ax,
        double ay,
        double bx,
        double by,
        double cx,
        double cy,
        string expectedHex)
    {
        Span<byte> actual = stackalloc byte[ShapePrimitiveCodec.PackedValueLength];

        ShapePrimitiveCodec.EncodeTriangle(
            actual,
            SpatialFieldKind.XYShape,
            new ShapeVertex(ax, ay),
            edgeAB: false,
            new ShapeVertex(bx, by),
            edgeBC: false,
            new ShapeVertex(cx, cy),
            edgeCA: false,
            valueOrdinal: 0);

        Assert.Equal(Convert.FromHexString(expectedHex), actual.ToArray());
        Assert.Equal((uint)expectedCode, BinaryPrimitives.ReadUInt32BigEndian(actual[24..]) & 0x7);

        ShapePrimitive decoded = ShapePrimitiveCodec.Decode(actual, SpatialFieldKind.XYShape);
        Assert.Equal((uint)0, decoded.ValueOrdinal);
        Assert.Equal(ShapePrimitiveKind.Triangle, decoded.Kind);
        Assert.True(SignedArea(decoded.A, decoded.B, decoded.C) > 0);
    }

    [Fact(DisplayName = "Point and line primitives use canonical degeneracy and boundary flags")]
    public void EncodePointAndLine_UseCanonicalForms()
    {
        Span<byte> pointBytes = stackalloc byte[ShapePrimitiveCodec.PackedValueLength];
        ShapePrimitiveCodec.EncodePoint(pointBytes, SpatialFieldKind.XYShape, new ShapeVertex(2, 3), 1);

        ShapePrimitive point = ShapePrimitiveCodec.Decode(pointBytes, SpatialFieldKind.XYShape);
        Assert.Equal(ShapePrimitiveKind.Point, point.Kind);
        Assert.Equal(point.A, point.B);
        Assert.Equal(point.A, point.C);
        Assert.True(point.EdgeAB && point.EdgeBC && point.EdgeCA);
        Assert.Equal((uint)1, point.ValueOrdinal);

        Span<byte> lineBytes = stackalloc byte[ShapePrimitiveCodec.PackedValueLength];
        ShapePrimitiveCodec.EncodeLine(
            lineBytes,
            SpatialFieldKind.XYShape,
            new ShapeVertex(8, -2),
            new ShapeVertex(-4, 5),
            valueOrdinal: 0);

        ShapePrimitive line = ShapePrimitiveCodec.Decode(lineBytes, SpatialFieldKind.XYShape);
        Assert.Equal(ShapePrimitiveKind.Line, line.Kind);
        Assert.Equal(line.A, line.C);
        Assert.NotEqual(line.A, line.B);
        Assert.True(line.EdgeAB && line.EdgeBC && line.EdgeCA);
    }

    [Fact(DisplayName = "Triangle source edge flags round-trip independently")]
    public void EncodeTriangle_RoundTripsEveryEdgeFlagCombination()
    {
        for (int mask = 0; mask < 8; mask++)
        {
            byte[] bytes = new byte[ShapePrimitiveCodec.PackedValueLength];
            ShapePrimitiveCodec.EncodeTriangle(
                bytes,
                SpatialFieldKind.XYShape,
                new ShapeVertex(0, 0),
                (mask & 1) != 0,
                new ShapeVertex(10, 10),
                (mask & 2) != 0,
                new ShapeVertex(5, 7),
                (mask & 4) != 0,
                valueOrdinal: 0);

            ShapePrimitive decoded = ShapePrimitiveCodec.Decode(bytes, SpatialFieldKind.XYShape);
            Assert.Equal((mask & 1) != 0, decoded.EdgeAB);
            Assert.Equal((mask & 2) != 0, decoded.EdgeBC);
            Assert.Equal((mask & 4) != 0, decoded.EdgeCA);
        }
    }

    [Fact(DisplayName = "Triangle encoding uses the minimum-X then minimum-Y first vertex")]
    public void EncodeTriangle_CanonicalisesTiedMinimumXAcrossCyclicRotations()
    {
        ShapeVertex[][] rotations =
        [
            [new ShapeVertex(0, 1), new ShapeVertex(1, 0), new ShapeVertex(0, 0)],
            [new ShapeVertex(1, 0), new ShapeVertex(0, 0), new ShapeVertex(0, 1)],
            [new ShapeVertex(0, 0), new ShapeVertex(0, 1), new ShapeVertex(1, 0)],
        ];
        byte[]? canonical = null;

        foreach (ShapeVertex[] rotation in rotations)
        {
            byte[] actual = new byte[ShapePrimitiveCodec.PackedValueLength];
            ShapePrimitiveCodec.EncodeTriangle(
                actual,
                SpatialFieldKind.XYShape,
                rotation[0], false,
                rotation[1], false,
                rotation[2], false,
                valueOrdinal: 0);

            canonical ??= actual;
            Assert.Equal(canonical, actual);
            ShapePrimitive decoded = ShapePrimitiveCodec.Decode(actual, SpatialFieldKind.XYShape);
            Assert.Equal(0, decoded.A.X);
            Assert.Equal(0, decoded.A.Y);
        }
    }

    [Theory(DisplayName = "Shape value ordinals use all 26 persisted bits")]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(0x03FF_FFFFu)]
    public void EncodePoint_PreservesValueOrdinal(uint ordinal)
    {
        Span<byte> bytes = stackalloc byte[ShapePrimitiveCodec.PackedValueLength];
        ShapePrimitiveCodec.EncodePoint(bytes, SpatialFieldKind.XYShape, new ShapeVertex(2, 3), ordinal);

        Assert.Equal(ordinal, ShapePrimitiveCodec.Decode(bytes, SpatialFieldKind.XYShape).ValueOrdinal);
        Assert.Equal((ordinal << 6) | 0x38u, BinaryPrimitives.ReadUInt32BigEndian(bytes[24..]));
    }

    [Fact(DisplayName = "Ordinal overflow is rejected before the destination is modified")]
    public void EncodePoint_RejectsOrdinalOverflowBeforeWriting()
    {
        byte[] bytes = new byte[ShapePrimitiveCodec.PackedValueLength];
        Array.Fill(bytes, (byte)0xA5);
        byte[] original = (byte[])bytes.Clone();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShapePrimitiveCodec.EncodePoint(
                bytes,
                SpatialFieldKind.XYShape,
                new ShapeVertex(2, 3),
                0x0400_0000u));

        Assert.Equal(original, bytes);
    }

    [Fact(DisplayName = "Decoder rejects altered bounds, orientation, non-canonical flags and non-finite XY values")]
    public void Decode_RejectsCorruptPrimitiveValues()
    {
        byte[] valid = new byte[ShapePrimitiveCodec.PackedValueLength];
        ShapePrimitiveCodec.EncodeTriangle(
            valid,
            SpatialFieldKind.XYShape,
            new ShapeVertex(0, 0), false,
            new ShapeVertex(10, 10), false,
            new ShapeVertex(5, 7), false,
            valueOrdinal: 0);

        byte[] badBounds = (byte[])valid.Clone();
        badBounds[0] = 0xFF;
        Assert.Throws<InvalidDataException>(() => ShapePrimitiveCodec.Decode(badBounds, SpatialFieldKind.XYShape));

        byte[] badOrientation = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(badOrientation.AsSpan(24), 1);
        Assert.Throws<InvalidDataException>(() => ShapePrimitiveCodec.Decode(badOrientation, SpatialFieldKind.XYShape));

        byte[] point = new byte[ShapePrimitiveCodec.PackedValueLength];
        ShapePrimitiveCodec.EncodePoint(point, SpatialFieldKind.XYShape, new ShapeVertex(2, 3), 0);
        BinaryPrimitives.WriteUInt32BigEndian(point.AsSpan(24), 0x30);
        Assert.Throws<InvalidDataException>(() => ShapePrimitiveCodec.Decode(point, SpatialFieldKind.XYShape));

        byte[] nonFinite = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(nonFinite.AsSpan(20), 0xFFC0_0000u);
        Assert.Throws<InvalidDataException>(() => ShapePrimitiveCodec.Decode(nonFinite, SpatialFieldKind.XYShape));
    }

    [Fact(DisplayName = "Decoder rejects non-canonical point and line metadata")]
    public void Decode_RejectsNonCanonicalDegeneratePrimitiveCodesAndFlags()
    {
        byte[] point = new byte[ShapePrimitiveCodec.PackedValueLength];
        ShapePrimitiveCodec.EncodePoint(point, SpatialFieldKind.XYShape, new ShapeVertex(2, 3), 0);
        BinaryPrimitives.WriteUInt32BigEndian(point.AsSpan(24), 1u | 0x38u);
        Assert.Throws<InvalidDataException>(() => ShapePrimitiveCodec.Decode(point, SpatialFieldKind.XYShape));

        byte[] line = new byte[ShapePrimitiveCodec.PackedValueLength];
        ShapePrimitiveCodec.EncodeLine(
            line,
            SpatialFieldKind.XYShape,
            new ShapeVertex(0, 0),
            new ShapeVertex(10, 10),
            valueOrdinal: 0);
        BinaryPrimitives.WriteUInt32BigEndian(line.AsSpan(24), 0u);
        Assert.Throws<InvalidDataException>(() => ShapePrimitiveCodec.Decode(line, SpatialFieldKind.XYShape));
    }

    private static double SignedArea(ShapeVertex a, ShapeVertex b, ShapeVertex c)
        => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
}
