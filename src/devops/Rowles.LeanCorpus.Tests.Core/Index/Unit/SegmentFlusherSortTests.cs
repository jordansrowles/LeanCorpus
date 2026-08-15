namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Unit tests for index-time sort permutation computation and the inverse remapping
/// ("rollback") applied to every field buffer in <see cref="SegmentFlusher"/>.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentFlusherSortTests
{
    [Fact]
    public void ComputeSortPermutation_NumericAscending()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.NumericDocValues["price"] = [30.0, 10.0, 20.0];

        var perm = SegmentFlusher.ComputeSortPermutation(
            new BufferFlushSource(buffer), new IndexSort(SortField.Numeric("price")));

        Assert.Equal(new[] { 1, 2, 0 }, perm);
    }

    [Fact]
    public void ComputeSortPermutation_NumericDescending()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.NumericDocValues["price"] = [30.0, 10.0, 20.0];

        var perm = SegmentFlusher.ComputeSortPermutation(
            new BufferFlushSource(buffer), new IndexSort(SortField.Numeric("price", descending: true)));

        Assert.Equal(new[] { 0, 2, 1 }, perm);
    }

    [Fact]
    public void ComputeSortPermutation_Int64Ascending()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.Int64DocValues["id"] = [300L, 100L, 200L];

        var perm = SegmentFlusher.ComputeSortPermutation(
            new BufferFlushSource(buffer), new IndexSort(SortField.Int64("id")));

        Assert.Equal(new[] { 1, 2, 0 }, perm);
    }

    [Fact]
    public void ComputeSortPermutation_StringAscending()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.SortedDocValues["name"] = ["c", "a", "b"];

        var perm = SegmentFlusher.ComputeSortPermutation(
            new BufferFlushSource(buffer), new IndexSort(SortField.String("name")));

        Assert.Equal(new[] { 1, 2, 0 }, perm);
    }

    [Fact]
    public void ComputeSortPermutation_MultiField_BreaksTiesBySecondary()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.NumericDocValues["price"] = [10.0, 10.0, 20.0];
        buffer.SortedDocValues["name"] = ["b", "a", "c"];

        var perm = SegmentFlusher.ComputeSortPermutation(
            new BufferFlushSource(buffer),
            new IndexSort(SortField.Numeric("price"), SortField.String("name")));

        Assert.Equal(new[] { 1, 0, 2 }, perm);
    }

    [Fact]
    public void ComputeSortPermutation_MultiValuedSelector_Max()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.SortedNumericDocValues["sn"] = new()
        {
            [0] = [1.0, 5.0],
            [1] = [2.0, 3.0],
            [2] = [4.0]
        };

        var perm = SegmentFlusher.ComputeSortPermutation(
            new BufferFlushSource(buffer),
            new IndexSort(SortField.SortedNumeric("sn", SortValueSelector.Max)));

        Assert.Equal(new[] { 1, 2, 0 }, perm);
    }

    [Fact]
    public void ComputeSortPermutation_FallsBackToNumericIndex()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.NumericIndex["price"] = new() { [0] = 30.0, [1] = 10.0, [2] = 20.0 };

        var perm = SegmentFlusher.ComputeSortPermutation(
            new BufferFlushSource(buffer),
            new IndexSort(SortField.Numeric("price")));

        Assert.Equal(new[] { 1, 2, 0 }, perm);
    }

    [Fact]
    public void ApplySortPermutation_RemapsAllFieldBuffers()
    {
        var buffer = new DocumentBufferState { DocCount = 3 };
        buffer.NumericDocValues["price"] = [10.0, 20.0, 30.0];
        buffer.Int64DocValues["id"] = [100L, 200L, 300L];
        buffer.SortedDocValues["name"] = ["a", "b", "c"];
        buffer.NumericIndex["score"] = new() { [0] = 1.5, [1] = 2.5, [2] = 3.5 };
        buffer.SortedSetDocValues["tags"] = new() { [0] = ["x"], [1] = ["y"], [2] = ["z"] };
        buffer.FieldBoosts["boost"] = new() { [0] = 1f, [1] = 2f, [2] = 3f };
        buffer.ParentDocIds = [0, 1];
        buffer.DocTokenCounts["body"] = [1, 2, 3];

        // sortPerm: new position -> old doc; inversePerm: old doc -> new position.
        var sortPerm = new[] { 1, 2, 0 };
        var inversePerm = new[] { 2, 0, 1 };

        SegmentFlusher.ApplySortPermutation(new BufferFlushSource(buffer), sortPerm, inversePerm);

        Assert.Equal(new[] { 20.0, 30.0, 10.0 }, buffer.NumericDocValues["price"]);
        Assert.Equal(new[] { 200L, 300L, 100L }, buffer.Int64DocValues["id"]);
        Assert.Equal(new[] { "b", "c", "a" }, buffer.SortedDocValues["name"]);

        Assert.Equal(2.5, buffer.NumericIndex["score"][0]);
        Assert.Equal(3.5, buffer.NumericIndex["score"][1]);
        Assert.Equal(1.5, buffer.NumericIndex["score"][2]);

        Assert.Equal(new[] { "y" }, buffer.SortedSetDocValues["tags"][0]);
        Assert.Equal(new[] { "z" }, buffer.SortedSetDocValues["tags"][1]);
        Assert.Equal(new[] { "x" }, buffer.SortedSetDocValues["tags"][2]);

        Assert.Equal(2f, buffer.FieldBoosts["boost"][0]);
        Assert.Equal(3f, buffer.FieldBoosts["boost"][1]);
        Assert.Equal(1f, buffer.FieldBoosts["boost"][2]);

        Assert.Equal(new[] { 2, 3, 1 }, buffer.DocTokenCounts["body"]);

        Assert.NotNull(buffer.ParentDocIds);
        Assert.Contains(0, buffer.ParentDocIds);
        Assert.Contains(2, buffer.ParentDocIds);
        Assert.DoesNotContain(1, buffer.ParentDocIds);
    }
}
