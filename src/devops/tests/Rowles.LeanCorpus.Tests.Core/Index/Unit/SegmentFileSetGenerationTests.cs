using Rowles.LeanCorpus.Index.Segment;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Verifies which deletion and statistics generations belong to a segment's selected generation, so readers
/// neither validate nor lease generations that commit pruning may delete at any moment.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentFileSetGenerationTests
{
    [Theory(DisplayName = "Unselected generation: superseded deletion and statistics files are excluded")]
    [InlineData("seg_3_gen_1.del", 2, true)]
    [InlineData("seg_3_gen_1.stats.json", 2, true)]
    [InlineData("seg_3_gen_3.del", 2, true)]
    [InlineData("seg_3.del", 2, true)]
    [InlineData("seg_3.stats.json", 2, true)]
    [InlineData("seg_3_gen_2.del", 2, false)]
    [InlineData("seg_3_gen_2.stats.json", 2, false)]
    [InlineData("seg_3.del", null, false)]
    [InlineData("seg_3.stats.json", null, false)]
    [InlineData("seg_3_gen_1.del", null, true)]
    [InlineData("seg_3.seg", 2, false)]
    [InlineData("seg_3.pos", 2, false)]
    [InlineData("seg_30_gen_1.del", 2, false)]
    public void IsUnselectedGenerationFile_MatchesPruningSelection(string fileName, int? selectedGeneration, bool expected)
        => Assert.Equal(expected, SegmentFileSet.IsUnselectedGenerationFile("seg_3", fileName, selectedGeneration));

    [Fact(DisplayName = "Snapshot selection: leases each segment's selected generation only")]
    public void IsSnapshotFile_SelectedGenerations_ExcludesOtherGenerationsAndSegments()
    {
        var selected = new Dictionary<string, int?>(StringComparer.Ordinal)
        {
            ["seg_3"] = 2,
            ["seg_4"] = null,
        };

        Assert.True(SegmentFileSet.IsSnapshotFile("seg_3.seg", selected));
        Assert.True(SegmentFileSet.IsSnapshotFile("seg_3_gen_2.del", selected));
        Assert.True(SegmentFileSet.IsSnapshotFile("seg_3_gen_2.stats.json", selected));
        Assert.True(SegmentFileSet.IsSnapshotFile("seg_4.stats.json", selected));
        Assert.False(SegmentFileSet.IsSnapshotFile("seg_3_gen_1.del", selected));
        Assert.False(SegmentFileSet.IsSnapshotFile("seg_3_gen_1.stats.json", selected));
        Assert.False(SegmentFileSet.IsSnapshotFile("seg_4_gen_1.del", selected));
        Assert.False(SegmentFileSet.IsSnapshotFile("seg_5.seg", selected));
        Assert.False(SegmentFileSet.IsSnapshotFile("seg_3.seg.tmp", selected));
        Assert.False(SegmentFileSet.IsSnapshotFile("segments_4", selected));
    }
}
