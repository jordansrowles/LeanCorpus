using System.Buffers;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Indexer.Postings;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class PreparedSpatialDocumentTests
{
    [Fact(DisplayName = "Owned shape preparation encodes once and reuses bytes for Packed BKD")]
    public void PrepareSpatialShapes_UsesSameEncodedBytesForPackedBkd()
    {
        var pool = new TrackingBytePool();
        using var dwpt = CreateDwpt(pool);
        var geometry = new XYRectangle(-10, -5, 20, 15);
        var document = new LeanDocument();
        document.Add(new XYShapeField("shape", geometry, storeDocValues: false));

        long before = dwpt.EstimatedRamBytes;
        PreparedSpatialDocument prepared = Assert.IsType<PreparedSpatialDocument>(dwpt.PrepareSpatialShapes(document));
        PreparedShapeValue value = Assert.Single(prepared.Values);
        byte[] expected = Encode(ShapeTessellator.PrepareXY(geometry, value.ValueOrdinal), SpatialFieldKind.XYShape);
        byte[] encoded = prepared.GetPackedPrimitives(value).ToArray();

        Assert.True(
            expected.AsSpan().SequenceEqual(encoded),
            $"Primitive count {value.PrimitiveCount}; expected {Convert.ToHexString(expected)}; actual {Convert.ToHexString(encoded)}.");
        Assert.Equal(prepared.AllocatedBytes, dwpt.PreparedSpatialAllocatedBytes);
        Assert.Equal(before + prepared.AllocatedBytes, dwpt.EstimatedRamBytes);

        dwpt.AddPrevalidatedDocument(document, prepared);

        PackedBkdFieldBuffer packedBuffer = dwpt.PackedBkdFields["shape"];
        for (int i = 0; i < value.PrimitiveCount; i++)
        {
            int sourceOffset = i * ShapePrimitiveCodec.PackedValueLength;
            int recordOffset = i * packedBuffer.Config.RecordBytes;
            Assert.Equal(
                expected.AsSpan(sourceOffset, ShapePrimitiveCodec.PackedValueLength).ToArray(),
                packedBuffer.Records.Slice(recordOffset, ShapePrimitiveCodec.PackedValueLength).ToArray());
        }
        Assert.Equal(0, dwpt.PreparedSpatialAllocatedBytes);
        Assert.Equal(pool.Rented.Count, pool.Returned.Count);
    }

    [Fact(DisplayName = "Prepared bytes transfer to retained Shape DocValues accounting")]
    public void AddPrevalidatedDocument_RetainsTheSameShapeDocValuesBytes()
    {
        var pool = new TrackingBytePool();
        using var dwpt = CreateDwpt(pool);
        var geometry = new XYRectangle(-10, -5, 20, 15);
        var document = new LeanDocument();
        document.Add(new XYShapeField("shape", geometry));

        using PreparedSpatialDocument prepared = Assert.IsType<PreparedSpatialDocument>(dwpt.PrepareSpatialShapes(document));
        PreparedShapeValue value = Assert.Single(prepared.Values);
        byte[] encoded = prepared.GetPackedPrimitives(value).ToArray();

        dwpt.AddPrevalidatedDocument(document, prepared);

        ShapeDocValuesFieldBuffer docValues = dwpt.ShapeDocValuesFields["shape"];
        ShapeDocValuesRecord record = Assert.Single(docValues.Records);
        Assert.Equal(encoded, docValues.GetPrimitives(record).ToArray());
        Assert.Equal(docValues.AllocatedBytes, dwpt.ShapeDocValuesAllocatedBytes);
        Assert.Equal(0, dwpt.PreparedSpatialAllocatedBytes);
        Assert.Equal(pool.Rented.Count, pool.Returned.Count);
    }

    [Fact(DisplayName = "Failed shape preparation returns its pooled bytes")]
    public void PrepareSpatialShapes_FailureDisposesEarlierEncodedComponents()
    {
        var pool = new TrackingBytePool();
        using var dwpt = CreateDwpt(pool, new IndexWriterConfig
        {
            DurableCommits = false,
            RamPerThreadHardLimitMB = 0.001,
        });
        XYPoint[] linePoints = Enumerable.Range(0, 81)
            .Select(static value => new XYPoint(value, value % 7))
            .ToArray();
        var document = new LeanDocument();
        document.Add(new XYShapeField(
            "shape",
            new XYGeometryCollection([new XYPoint(1, 2), new XYLineString(linePoints)])));
        long before = dwpt.EstimatedRamBytes;

        Assert.Throws<InvalidOperationException>(() => dwpt.PrepareSpatialShapes(document));

        Assert.NotEmpty(pool.Rented);
        Assert.Equal(pool.Rented.Count, pool.Returned.Count);
        Assert.Equal(0, dwpt.PreparedSpatialAllocatedBytes);
        Assert.Equal(before, dwpt.EstimatedRamBytes);
        Assert.Empty(dwpt.PackedBkdFields);
    }

    [Fact(DisplayName = "Large shape preparation owns encoded capacity without primitive objects")]
    public void PrepareSpatialShapes_LargeLineUsesBoundedEncodedStorage()
    {
        using var dwpt = CreateDwpt(ArrayPool<byte>.Shared);
        XYPoint[] points = Enumerable.Range(0, 8_001)
            .Select(static value => new XYPoint(value, value % 17))
            .ToArray();
        var document = new LeanDocument();
        document.Add(new XYShapeField("line", new XYLineString(points), storeDocValues: false));

        using PreparedSpatialDocument prepared = Assert.IsType<PreparedSpatialDocument>(dwpt.PrepareSpatialShapes(document));
        PreparedShapeValue value = Assert.Single(prepared.Values);

        Assert.Equal(points.Length - 1, value.PrimitiveCount);
        Assert.Equal(value.PrimitiveCount * ShapePrimitiveCodec.PackedValueLength, prepared.GetPackedPrimitives(value).Length);
        Assert.True(prepared.AllocatedBytes >= prepared.GetPackedPrimitives(value).Length);
        Assert.True(prepared.AllocatedBytes < prepared.GetPackedPrimitives(value).Length * 2L);
        Assert.Equal(prepared.AllocatedBytes, dwpt.PreparedSpatialAllocatedBytes);
    }

    [Fact(DisplayName = "Repeated shape values preserve ordinals and require consistent DocValues settings")]
    public void PrepareSpatialShapes_PreservesOrdinalsAndRejectsConflictingDocValues()
    {
        using var dwpt = CreateDwpt(ArrayPool<byte>.Shared);
        var document = new LeanDocument();
        document.Add(new LatLonShapeField("geo", new GeoPoint(1, 2)));
        document.Add(new LatLonShapeField("geo", new GeoPoint(3, 4)));

        using PreparedSpatialDocument prepared = Assert.IsType<PreparedSpatialDocument>(dwpt.PrepareSpatialShapes(document));

        Assert.Equal([0u, 1u], prepared.Values.Select(static value => value.ValueOrdinal));
        Assert.All(prepared.Values, static value => Assert.True(value.StoreDocValues));

        var conflicting = new LeanDocument();
        conflicting.Add(new LatLonShapeField("geo", new GeoPoint(1, 2)));
        conflicting.Add(new LatLonShapeField("geo", new GeoPoint(3, 4), storeDocValues: false));
        Assert.Throws<InvalidOperationException>(() => dwpt.ValidateDocument(conflicting));
        Assert.Empty(dwpt.PackedBkdFields);
    }

    private static DocumentsWriterPerThread CreateDwpt(
        ArrayPool<byte> shapePool,
        IndexWriterConfig? config = null)
        => new(
            new WhitespaceAnalyser(),
            new Dictionary<string, IAnalyser>(),
            config ?? new IndexWriterConfig { DurableCommits = false },
            ArrayPool<PostingTermState>.Shared,
            ArrayPool<byte>.Shared,
            shapePool);

    private static byte[] Encode(IReadOnlyList<ShapePrimitive> primitives, SpatialFieldKind kind)
    {
        byte[] bytes = new byte[checked(primitives.Count * ShapePrimitiveCodec.PackedValueLength)];
        for (int i = 0; i < primitives.Count; i++)
        {
            ShapePrimitive primitive = primitives[i];
            Span<byte> target = bytes.AsSpan(i * ShapePrimitiveCodec.PackedValueLength, ShapePrimitiveCodec.PackedValueLength);
            switch (primitive.Kind)
            {
                case ShapePrimitiveKind.Point:
                    ShapePrimitiveCodec.EncodePoint(target, kind, primitive.A, primitive.ValueOrdinal);
                    break;
                case ShapePrimitiveKind.Line:
                    ShapePrimitiveCodec.EncodeLine(target, kind, primitive.A, primitive.B, primitive.ValueOrdinal);
                    break;
                case ShapePrimitiveKind.Triangle:
                    ShapePrimitiveCodec.EncodeTriangle(
                        target, kind,
                        primitive.A, primitive.EdgeAB,
                        primitive.B, primitive.EdgeBC,
                        primitive.C, primitive.EdgeCA,
                        primitive.ValueOrdinal);
                    break;
            }
        }
        return bytes;
    }

    private sealed class TrackingBytePool : ArrayPool<byte>
    {
        internal List<byte[]> Rented { get; } = [];
        internal List<byte[]> Returned { get; } = [];

        public override byte[] Rent(int minimumLength)
        {
            var result = new byte[minimumLength];
            Rented.Add(result);
            return result;
        }

        public override void Return(byte[] array, bool clearArray = false)
            => Returned.Add(array);
    }
}
