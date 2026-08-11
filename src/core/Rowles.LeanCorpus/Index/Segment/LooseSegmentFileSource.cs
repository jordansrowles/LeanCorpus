using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Provides logical segment files stored directly in an index directory.</summary>
internal sealed class LooseSegmentFileSource : ISegmentFileSource
{
    private readonly MMapDirectory _directory;
    private readonly IReadOnlyList<string> _fileNames;

    internal LooseSegmentFileSource(MMapDirectory directory, string segmentId)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);

        _directory = directory;
        _fileNames = Array.AsReadOnly(directory.ListAll()
            .Where(fileName => IsSegmentFile(fileName, segmentId))
            .OrderBy(static fileName => fileName, StringComparer.Ordinal)
            .ToArray());
    }

    public IReadOnlyList<string> EnumerateFiles() => _fileNames;

    public bool FileExists(string fileName) => _directory.FileExists(fileName);

    public long GetFileLength(string fileName)
        => FileOpenRetry.GetFileLength(Path.Combine(_directory.DirectoryPath, fileName));

    public IndexInput OpenInput(string fileName) => _directory.OpenInput(fileName);

    public void Dispose()
    {
    }

    internal static bool IsSegmentFile(string fileName, string segmentId)
        => fileName.StartsWith(segmentId + ".", StringComparison.Ordinal) ||
           fileName.StartsWith(segmentId + "_v_", StringComparison.Ordinal) ||
           fileName.StartsWith(segmentId + "_gen_", StringComparison.Ordinal) &&
           fileName.EndsWith(".del", StringComparison.OrdinalIgnoreCase);
}
