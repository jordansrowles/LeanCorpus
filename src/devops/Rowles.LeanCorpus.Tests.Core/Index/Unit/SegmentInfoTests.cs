using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Unit tests for <see cref="SegmentInfo.ReadFrom"/> error branches.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentInfoTests : IDisposable
{
    private readonly string _dir;

    public SegmentInfoTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_seg_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    [Fact(DisplayName = "SegmentInfo.ReadFrom: JSON Null Deserialise Throws InvalidDataException")]
    public void ReadFrom_NullJsonDeserialise_ThrowsInvalidDataException()
    {
        var path = Path.Combine(_dir, "null.seg");
        File.WriteAllText(path, "null");

        Assert.Throws<InvalidDataException>(() => SegmentInfo.ReadFrom(path));
    }

    [Fact(DisplayName = "SegmentInfo.ReadFrom: Valid File Returns SegmentInfo")]
    public void ReadFrom_ValidFile_ReturnsSegmentInfo()
    {
        var info = new SegmentInfo
        {
            SegmentId = "seg_0",
            DocCount = 5,
            LiveDocCount = 5,
        };
        var path = Path.Combine(_dir, "seg_0.seg");
        info.WriteTo(path);

        var loaded = SegmentInfo.ReadFrom(path);
        Assert.Equal("seg_0", loaded.SegmentId);
        Assert.Equal(5, loaded.DocCount);
    }

    [Fact(DisplayName = "SegmentInfo persists spatial field kinds and reads legacy metadata without them")]
    public void ReadFrom_SpatialMetadata_RoundTripsAndDefaultsForLegacy()
    {
        var info = new SegmentInfo
        {
            SegmentId = "seg_spatial",
            DocCount = 1,
            LiveDocCount = 1,
            SpatialFields =
            [
                new SpatialFieldInfo { FieldName = "geo", Kind = SpatialFieldKind.GeoShape },
                new SpatialFieldInfo { FieldName = "xy", Kind = SpatialFieldKind.XYPoint },
            ],
        };
        string spatialPath = Path.Combine(_dir, "spatial.seg");
        info.WriteTo(spatialPath);

        SegmentInfo loaded = SegmentInfo.ReadFrom(spatialPath);
        Assert.Collection(
            loaded.SpatialFields,
            field =>
            {
                Assert.Equal("geo", field.FieldName);
                Assert.Equal(SpatialFieldKind.GeoShape, field.Kind);
            },
            field =>
            {
                Assert.Equal("xy", field.FieldName);
                Assert.Equal(SpatialFieldKind.XYPoint, field.Kind);
            });

        string legacyPath = Path.Combine(_dir, "legacy.seg");
        File.WriteAllText(
            legacyPath,
            "{\"SegmentId\":\"seg_legacy\",\"FieldNames\":[],\"VectorFields\":[],\"CodecBytes\":{}}");
        Assert.Empty(SegmentInfo.ReadFrom(legacyPath).SpatialFields);
    }

    [Theory(DisplayName = "SegmentInfo rejects duplicate, invalid-name and undefined spatial metadata")]
    [InlineData(false, "valid", (byte)0)]
    [InlineData(false, "valid", (byte)99)]
    [InlineData(false, "bad\nname", (byte)1)]
    [InlineData(true, "valid", (byte)1)]
    public void ReadFrom_InvalidSpatialMetadata_ThrowsInvalidDataException(bool duplicate, string fieldName, byte kind)
    {
        var fields = new List<SpatialFieldInfo>
        {
            new() { FieldName = fieldName, Kind = (SpatialFieldKind)kind },
        };
        if (duplicate)
            fields.Add(new SpatialFieldInfo { FieldName = fieldName, Kind = SpatialFieldKind.GeoPoint });

        var info = new SegmentInfo
        {
            SegmentId = "seg_invalid",
            SpatialFields = fields,
        };
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".seg");
        info.WriteTo(path);

        Assert.Throws<InvalidDataException>(() => SegmentInfo.ReadFrom(path));
    }
}
