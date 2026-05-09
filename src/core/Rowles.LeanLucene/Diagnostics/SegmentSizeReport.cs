namespace Rowles.LeanLucene.Diagnostics;

/// <summary>Per-segment size breakdown.</summary>
public sealed class SegmentSizeReport
{
    /// <summary>Gets the name of the segment (e.g., <c>seg_0</c>).</summary>
    public required string SegmentName { get; init; }

    /// <summary>Gets the total on-disk size of all files belonging to this segment in bytes.</summary>
    public long TotalSizeBytes { get; private set; }
    private readonly Dictionary<string, long> _fileSizes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-extension file sizes.</summary>
    public IReadOnlyDictionary<string, long> FileSizes => _fileSizes;

    internal void AddFile(string extension, long size)
    {
        _fileSizes[extension] = _fileSizes.GetValueOrDefault(extension) + size;
        TotalSizeBytes += size;
    }
}
