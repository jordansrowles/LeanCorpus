using Rowles.LeanLucene.Diagnostics;

namespace Rowles.LeanLucene.Tests.Diagnostics;

/// <summary>
/// Unit tests covering the IndexSizeReport.TotalSizeFormatted computed property,
/// which exercises all four FormatBytes branches (B, KB, MB, GB).
/// Also covers SegmentSizeReport.AddFile (internal) and the SegmentDataSizeBytes
/// and SegmentCount computed properties.
/// </summary>
public sealed class IndexSizeReportFormatTests
{
    // ── FormatBytes branches ──────────────────────────────────────────────────

    /// <summary>Verifies TotalSizeFormatted returns bytes label for sub-1024 size.</summary>
    [Fact(DisplayName = "FormatBytes: Sub 1024 Returns Bytes Label")]
    public void FormatBytes_Sub1024_ReturnsBytesLabel()
    {
        var report = new IndexSizeReport { DirectoryPath = "test", TotalSizeBytes = 500 };
        Assert.EndsWith(" B", report.TotalSizeFormatted);
        Assert.StartsWith("500", report.TotalSizeFormatted);
    }

    /// <summary>Verifies TotalSizeFormatted returns KB label for sub-1MB size.</summary>
    [Fact(DisplayName = "FormatBytes: Sub 1MB Returns KB Label")]
    public void FormatBytes_Sub1Mb_ReturnsKbLabel()
    {
        var report = new IndexSizeReport { DirectoryPath = "test", TotalSizeBytes = 2048 };
        Assert.EndsWith(" KB", report.TotalSizeFormatted);
    }

    /// <summary>Verifies TotalSizeFormatted returns MB label for sub-1GB size.</summary>
    [Fact(DisplayName = "FormatBytes: Sub 1GB Returns MB Label")]
    public void FormatBytes_Sub1Gb_ReturnsMbLabel()
    {
        var report = new IndexSizeReport { DirectoryPath = "test", TotalSizeBytes = 5L * 1024 * 1024 };
        Assert.EndsWith(" MB", report.TotalSizeFormatted);
    }

    /// <summary>Verifies TotalSizeFormatted returns GB label for 1GB+ size.</summary>
    [Fact(DisplayName = "FormatBytes: 1GB Or More Returns GB Label")]
    public void FormatBytes_1GbOrMore_ReturnsGbLabel()
    {
        var report = new IndexSizeReport { DirectoryPath = "test", TotalSizeBytes = 2L * 1024 * 1024 * 1024 };
        Assert.EndsWith(" GB", report.TotalSizeFormatted);
    }

    // ── Computed properties ───────────────────────────────────────────────────

    /// <summary>Verifies SegmentCount reflects the number of segments.</summary>
    [Fact(DisplayName = "IndexSizeReport: SegmentCount Reflects Segment List")]
    public void IndexSizeReport_SegmentCount_ReflectsSegmentList()
    {
        var seg1 = new SegmentSizeReport { SegmentName = "seg_0" };
        var seg2 = new SegmentSizeReport { SegmentName = "seg_1" };

        var report = new IndexSizeReport
        {
            DirectoryPath = "test",
            Segments = [seg1, seg2]
        };

        Assert.Equal(2, report.SegmentCount);
    }

    /// <summary>Verifies SegmentDataSizeBytes is the sum of all segment sizes.</summary>
    [Fact(DisplayName = "IndexSizeReport: SegmentDataSizeBytes Is Sum Of Segments")]
    public void IndexSizeReport_SegmentDataSizeBytes_IsSumOfSegments()
    {
        var seg1 = new SegmentSizeReport { SegmentName = "seg_0" };
        seg1.AddFile(".dic", 1000);
        seg1.AddFile(".pos", 500);

        var seg2 = new SegmentSizeReport { SegmentName = "seg_1" };
        seg2.AddFile(".dic", 2000);

        var report = new IndexSizeReport
        {
            DirectoryPath = "test",
            Segments = [seg1, seg2]
        };

        Assert.Equal(3500, report.SegmentDataSizeBytes);
    }

    // ── SegmentSizeReport.AddFile ─────────────────────────────────────────────

    /// <summary>Verifies AddFile accumulates size for the same extension.</summary>
    [Fact(DisplayName = "SegmentSizeReport: AddFile Accumulates Same Extension")]
    public void SegmentSizeReport_AddFile_AccumulatesSameExtension()
    {
        var seg = new SegmentSizeReport { SegmentName = "seg_0" };
        seg.AddFile(".dic", 1000);
        seg.AddFile(".dic", 500);

        Assert.Equal(1500, seg.FileSizes[".dic"]);
        Assert.Equal(1500, seg.TotalSizeBytes);
    }

    /// <summary>Verifies AddFile tracks multiple distinct extensions.</summary>
    [Fact(DisplayName = "SegmentSizeReport: AddFile Tracks Multiple Extensions")]
    public void SegmentSizeReport_AddFile_TracksMultipleExtensions()
    {
        var seg = new SegmentSizeReport { SegmentName = "seg_0" };
        seg.AddFile(".dic", 1000);
        seg.AddFile(".pos", 2000);
        seg.AddFile(".fdt", 3000);

        Assert.Equal(3, seg.FileSizes.Count);
        Assert.Equal(6000, seg.TotalSizeBytes);
    }

    /// <summary>Verifies a fresh SegmentSizeReport starts with zero size.</summary>
    [Fact(DisplayName = "SegmentSizeReport: Fresh Instance Has Zero Size")]
    public void SegmentSizeReport_FreshInstance_HasZeroSize()
    {
        var seg = new SegmentSizeReport { SegmentName = "seg_0" };
        Assert.Equal(0, seg.TotalSizeBytes);
        Assert.Empty(seg.FileSizes);
    }
}
