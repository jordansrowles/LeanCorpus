namespace Rowles.LeanLucene.Diagnostics;

/// <summary>Overall index size report.</summary>
public sealed class IndexSizeReport
{
    /// <summary>Gets the file system path of the index directory.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Gets the total on-disk size of all index files in bytes.</summary>
    public long TotalSizeBytes { get; init; }

    /// <summary>Gets the size of commit (segments_N) files in bytes.</summary>
    public long CommitFileSizeBytes { get; init; }

    /// <summary>Gets the size of statistics (stats_N.json) files in bytes.</summary>
    public long StatsFileSizeBytes { get; init; }

    /// <summary>Gets the per-segment size breakdowns.</summary>
    public IReadOnlyList<SegmentSizeReport> Segments { get; init; } = [];

    /// <summary>Gets the total size contributed by all segment data files in bytes.</summary>
    public long SegmentDataSizeBytes => Segments.Sum(s => s.TotalSizeBytes);

    /// <summary>Gets the number of segments in the index.</summary>
    public int SegmentCount => Segments.Count;

    /// <summary>Human-readable total size.</summary>
    public string TotalSizeFormatted => FormatBytes(TotalSizeBytes);

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F1} MB",
        _ => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };
}
