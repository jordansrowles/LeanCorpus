namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>Tests selection of immutable files retained by segment snapshots.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class SegmentReaderFileSelectionTests
{
    [Fact(DisplayName = "SegmentReader file selection excludes atomic temporary files")]
    public void SelectSegmentFiles_AtomicTemporaryFiles_ExcludesTemporaryFiles()
    {
        string[] inventory =
        [
            "seg_0.seg",
            "seg_0.pos",
            "seg_0_gen_2.del",
            "seg_0.seg.0123456789abcdef0123456789abcdef.tmp",
            "seg_0_gen_2.del.0123456789abcdef0123456789abcdef.tmp",
            "seg_1.pos",
        ];

        var selected = SegmentReader.SelectSegmentFiles("seg_0", inventory);

        Assert.Equal(["seg_0.seg", "seg_0.pos", "seg_0_gen_2.del"], selected);
    }
}
