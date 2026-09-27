using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Unit tests for <see cref="SegmentInfo.ReadFrom"/> error branches.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentInfoTests : IDisposable
{
    public static TheoryData<string, SegmentInfo> InvalidMetadataCases { get; } = new()
    {
        { "negative document count", new SegmentInfo { SegmentId = "seg_negative_doc_count", DocCount = -1 } },
        { "negative live document count", new SegmentInfo { SegmentId = "seg_negative_live_count", DocCount = 1, LiveDocCount = -1 } },
        { "live document count exceeds document count", new SegmentInfo { SegmentId = "seg_excess_live_count", DocCount = 1, LiveDocCount = 2 } },
        { "negative total bytes", new SegmentInfo { SegmentId = "seg_negative_bytes", TotalBytes = -1 } },
        { "negative codec bytes", new SegmentInfo { SegmentId = "seg_negative_codec_bytes", CodecBytes = new Dictionary<string, long> { [".seg"] = -1 } } },
        { "negative commit generation", new SegmentInfo { SegmentId = "seg_negative_commit_generation", CommitGeneration = -1 } },
        { "negative deletion generation", new SegmentInfo { SegmentId = "seg_negative_delete_generation", DelGeneration = -1 } },
        { "negative soft-delete timestamp", new SegmentInfo { SegmentId = "seg_negative_soft_delete", EarliestSoftDeleteTimestamp = -1 } },
        { "negative minimum sequence number", new SegmentInfo { SegmentId = "seg_negative_min_sequence", MinSequenceNumber = -1, MaxSequenceNumber = 0 } },
        { "negative maximum sequence number", new SegmentInfo { SegmentId = "seg_negative_max_sequence", MinSequenceNumber = 0, MaxSequenceNumber = -1 } },
        { "inverted sequence range", new SegmentInfo { SegmentId = "seg_inverted_sequence_range", MinSequenceNumber = 2, MaxSequenceNumber = 1 } },
        { "missing sequence range endpoint", new SegmentInfo { SegmentId = "seg_partial_sequence_range", MinSequenceNumber = 0 } },
        { "empty field name", new SegmentInfo { SegmentId = "seg_empty_field_name", FieldNames = [""] } },
        { "duplicate field name", new SegmentInfo { SegmentId = "seg_duplicate_field_name", FieldNames = ["title", "title"] } },
        { "duplicate vector field name", new SegmentInfo { SegmentId = "seg_duplicate_vector", VectorFields = [new() { FieldName = "embedding", Dimension = 3 }, new() { FieldName = "embedding", Dimension = 3 }] } },
        { "undefined vector quantisation", new SegmentInfo { SegmentId = "seg_invalid_quantisation", VectorFields = [new() { FieldName = "embedding", Dimension = 3, Quantisation = (Rowles.LeanCorpus.Codecs.Vectors.VectorQuantisation)255 }] } },
        { "zero vector dimension", new SegmentInfo { SegmentId = "seg_zero_vector_dimension", VectorFields = [new() { FieldName = "embedding", Dimension = 0 }] } },
        { "malformed index sort", new SegmentInfo { SegmentId = "seg_bad_sort_format", IndexSortFields = ["Numeric:price"] } },
        { "undefined index sort type", new SegmentInfo { SegmentId = "seg_bad_sort_type", IndexSortFields = ["127:price:False"] } },
        { "unsupported index sort type", new SegmentInfo { SegmentId = "seg_score_sort", IndexSortFields = ["Score::False"] } },
        { "empty index sort list", new SegmentInfo { SegmentId = "seg_empty_sort", IndexSortFields = [] } },
    };

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

    [Theory(DisplayName = "SegmentInfo.ReadFrom: Invalid Descriptor Metadata Is Rejected")]
    [MemberData(nameof(InvalidMetadataCases))]
    public void ReadFrom_InvalidDescriptorMetadata_ThrowsInvalidDataException(string caseName, SegmentInfo info)
    {
        string path = Path.Combine(_dir, caseName.Replace(' ', '_') + ".seg");
        info.WriteTo(path);

        Assert.Throws<InvalidDataException>(() => SegmentInfo.ReadFrom(path));
    }

    [Fact(DisplayName = "SegmentInfo.ReadFrom: Corrupt Metadata Does Not Poison Later Reads")]
    public void ReadFrom_CorruptMetadata_DoesNotPoisonLaterReads()
    {
        var corruptPath = Path.Combine(_dir, "seg_corrupt_metadata.seg");
        new SegmentInfo
        {
            SegmentId = "seg_corrupt_metadata",
            DocCount = 1,
            LiveDocCount = 2
        }.WriteTo(corruptPath);

        Assert.Throws<InvalidDataException>(() => SegmentInfo.ReadFrom(corruptPath));

        string malformedPath = Path.Combine(_dir, "seg_malformed_json.seg");
        File.WriteAllText(malformedPath, "{\"SegmentId\":");
        Assert.Throws<InvalidDataException>(() => SegmentInfo.ReadFrom(malformedPath));

        var validPath = Path.Combine(_dir, "seg_valid_after_corruption.seg");
        new SegmentInfo
        {
            SegmentId = "seg_valid_after_corruption",
            DocCount = 1,
            LiveDocCount = 1
        }.WriteTo(validPath);

        Assert.Equal("seg_valid_after_corruption", SegmentInfo.ReadFrom(validPath).SegmentId);
    }

    [Fact(DisplayName = "SegmentDescriptor: Rejects Invalid Mutable Segment Metadata")]
    public void SegmentDescriptor_InvalidMutableMetadata_ThrowsInvalidDataException()
    {
        var info = new SegmentInfo
        {
            SegmentId = "seg_invalid_descriptor",
            DocCount = 1,
            LiveDocCount = 2
        };

        Assert.Throws<InvalidDataException>(() => new SegmentDescriptor(info));
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
