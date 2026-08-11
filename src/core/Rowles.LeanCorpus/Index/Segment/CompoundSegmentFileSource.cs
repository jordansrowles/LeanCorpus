using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Provides compound members and the segment's external sidecars as logical files.</summary>
internal sealed class CompoundSegmentFileSource : ISegmentFileSource
{
    private readonly LooseSegmentFileSource _looseFiles;
    private readonly MMapDirectory _directory;
    private readonly CompoundFileReader _compound;
    private readonly IReadOnlyList<string> _fileNames;

    internal CompoundSegmentFileSource(MMapDirectory directory, string segmentId)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentId);

        _directory = directory;
        _looseFiles = new LooseSegmentFileSource(directory, segmentId);
        _compound = CompoundFileReader.Open(directory, segmentId + ".cfs");
        _fileNames = Array.AsReadOnly(_looseFiles.EnumerateFiles()
            .Where(fileName => !fileName.Equals(segmentId + ".cfs", StringComparison.Ordinal))
            .Concat(_compound.FileNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static fileName => fileName, StringComparer.Ordinal)
            .ToArray());
    }

    public IReadOnlyList<string> EnumerateFiles() => _fileNames;

    public bool FileExists(string fileName)
        => _compound.HasFile(fileName) || _looseFiles.FileExists(fileName);

    public long GetFileLength(string fileName)
        => _compound.HasFile(fileName)
            ? _compound.GetFileLength(fileName)
            : _looseFiles.GetFileLength(fileName);

    public IndexInput OpenInput(string fileName)
        => _compound.HasFile(fileName)
            ? _compound.OpenInput(_directory, fileName)
            : _looseFiles.OpenInput(fileName);

    public void Dispose()
    {
        _compound.Dispose();
        _looseFiles.Dispose();
    }
}
