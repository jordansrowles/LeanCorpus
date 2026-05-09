namespace Rowles.LeanLucene.Diagnostics;

/// <summary>
/// Calculates and reports the on-disk size of an index and its segments.
/// </summary>
public static class IndexSizeCalculator
{
    private static readonly string[] SegmentExtensions =
        [".seg", ".dic", ".pos", ".fdt", ".fdx", ".nrm", ".fln", ".num", ".dvn", ".dvs",
         ".bkd", ".del", ".tvd", ".tvx", ".vec"];

    /// <summary>
    /// Calculates the total index size and per-segment breakdown.
    /// </summary>
    public static IndexSizeReport Calculate(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
            throw new DirectoryNotFoundException($"Index directory not found: {directoryPath}");

        var allFiles = Directory.GetFiles(directoryPath);
        long totalSize = 0;
        long commitFileSize = 0;
        long statsFileSize = 0;
        var segmentReports = new Dictionary<string, SegmentSizeReport>(StringComparer.Ordinal);

        foreach (var file in allFiles)
        {
            var fi = new FileInfo(file);
            long size = fi.Length;
            totalSize += size;
            string name = fi.Name;

            if (name.StartsWith("segments_", StringComparison.Ordinal))
            {
                commitFileSize += size;
                continue;
            }

            if (name.StartsWith("stats_", StringComparison.Ordinal))
            {
                statsFileSize += size;
                continue;
            }

            // Extract segment name (e.g., "seg_0" from "seg_0.dic")
            string ext = fi.Extension;
            if (string.IsNullOrEmpty(ext)) continue;

            string segName = Path.GetFileNameWithoutExtension(name);
            if (!segmentReports.TryGetValue(segName, out var segReport))
            {
                segReport = new SegmentSizeReport { SegmentName = segName };
                segmentReports[segName] = segReport;
            }
            segReport.AddFile(ext, size);
        }

        return new IndexSizeReport
        {
            DirectoryPath = directoryPath,
            TotalSizeBytes = totalSize,
            CommitFileSizeBytes = commitFileSize,
            StatsFileSizeBytes = statsFileSize,
            Segments = segmentReports.Values.OrderBy(s => s.SegmentName).ToList()
        };
    }
}
