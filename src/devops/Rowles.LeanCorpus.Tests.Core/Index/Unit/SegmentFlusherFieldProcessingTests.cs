namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Unit tests for the pure field-processing helpers in <see cref="SegmentFlusher"/>:
/// multi-value sort selection, dense column materialisation, and vector quantisation parameters.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentFlusherFieldProcessingTests
{
    [Fact]
    public void SelectNumericValue_Min_ReturnsLowest()
    {
        Assert.Equal(1.5, SegmentFlusher.SelectNumericValue(new double[] { 3.5, 1.5, 2.5 }, SortValueSelector.Min));
    }

    [Fact]
    public void SelectNumericValue_Max_ReturnsHighest()
    {
        Assert.Equal(3.5, SegmentFlusher.SelectNumericValue(new double[] { 3.5, 1.5, 2.5 }, SortValueSelector.Max));
    }

    [Fact]
    public void SelectInt64Value_Min_ReturnsLowest()
    {
        Assert.Equal(10L, SegmentFlusher.SelectInt64Value(new long[] { 30L, 10L, 20L }, SortValueSelector.Min));
    }

    [Fact]
    public void SelectInt64Value_Max_ReturnsHighest()
    {
        Assert.Equal(30L, SegmentFlusher.SelectInt64Value(new long[] { 30L, 10L, 20L }, SortValueSelector.Max));
    }

    [Fact]
    public void ToDenseMultiValueColumns_DensifiesSparseColumns()
    {
        var source = new Dictionary<string, Dictionary<int, List<string>>>
        {
            ["tags"] = new() { [0] = ["a", "b"], [2] = ["c"] }
        };

        var dense = SegmentFlusher.ToDenseMultiValueColumns(source, 3);

        var tags = dense["tags"];
        Assert.NotNull(tags);
        Assert.Equal(new[] { "a", "b" }, tags[0]);
        Assert.Null(tags[1]);
        Assert.Equal(new[] { "c" }, tags[2]);
    }

    [Fact]
    public void ToDenseMultiValueColumns_SkipsOutOfRangeAndEmpty()
    {
        var source = new Dictionary<string, Dictionary<int, List<string>>>
        {
            ["tags"] = new() { [5] = ["x"], [1] = [] }
        };

        var dense = SegmentFlusher.ToDenseMultiValueColumns(source, 3);

        // No document carried a valid in-range, non-empty value, so the field is omitted.
        Assert.Empty(dense);
    }

    [Fact]
    public void ToDenseMultiValueColumns_EmptySource_ReturnsEmpty()
    {
        var dense = SegmentFlusher.ToDenseMultiValueColumns(
            new Dictionary<string, Dictionary<int, List<int>>>(), 10);

        Assert.Empty(dense);
    }

    [Fact]
    public void ComputeInt8Params_ReturnsMinAndAlpha()
    {
        var perField = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { -2f, 0f },
            [1] = new float[] { 4f, 8f }
        };

        var (min, alpha) = SegmentFlusher.ComputeInt8Params(perField);

        Assert.Equal(-2f, min);
        Assert.Equal(10f / 255f, alpha);
    }

    [Fact]
    public void ComputeInt8Params_DegenerateRange_ExpandsAlpha()
    {
        var perField = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 5f, 5f, 5f }
        };

        var (min, alpha) = SegmentFlusher.ComputeInt8Params(perField);

        Assert.Equal(5f, min);
        Assert.Equal(1f / 255f, alpha);
    }

    [Fact]
    public void ComputeBBQCentroid_AveragesPerDimension()
    {
        var perField = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1f, 3f },
            [1] = new float[] { 5f, 7f }
        };

        var centroid = SegmentFlusher.ComputeBBQCentroid(perField, 2);

        Assert.Equal(new[] { 3f, 5f }, centroid);
    }

    [Fact]
    public void ComputeBBQCentroid_EmptyInput_ReturnsZeroCentroid()
    {
        var centroid = SegmentFlusher.ComputeBBQCentroid(
            new Dictionary<int, ReadOnlyMemory<float>>(), 3);

        Assert.Equal(new float[] { 0f, 0f, 0f }, centroid);
    }
}
