using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Resolves direct or compound segment members without exposing storage details to codecs.</summary>
internal sealed class SegmentFileAccess : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly string _segmentId;
    private readonly ISegmentFileSource _fileSource;
    private readonly bool _isCompound;

    private SegmentFileAccess(MMapDirectory directory, string segmentId, ISegmentFileSource fileSource, bool isCompound)
    {
        _directory = directory;
        _segmentId = segmentId;
        _fileSource = fileSource;
        _isCompound = isCompound;
    }

    internal static SegmentFileAccess Open(MMapDirectory directory, SegmentInfo info)
    {
        if (!info.IsCompoundFile)
        {
            return new SegmentFileAccess(
                directory,
                info.SegmentId,
                new LooseSegmentFileSource(directory, info.SegmentId),
                isCompound: false);
        }

        string cfsName = info.SegmentId + ".cfs";
        if (!directory.FileExists(cfsName))
            throw new FileNotFoundException($"Compound segment file is missing: '{cfsName}'.", cfsName);
        return new SegmentFileAccess(
            directory,
            info.SegmentId,
            new CompoundSegmentFileSource(directory, info.SegmentId),
            isCompound: true);
    }

    internal bool IsCompound => _isCompound;

    internal string Name(string extension) => _segmentId + extension;

    internal bool Exists(string extension)
    {
        string name = Name(extension);
        return _fileSource.FileExists(name);
    }

    internal IndexInput OpenInput(string extension)
    {
        string name = Name(extension);
        return _fileSource.OpenInput(name);
    }

    internal string DirectPath(string extension)
        => Path.Combine(_directory.DirectoryPath, Name(extension));

    public void Dispose() => _fileSource.Dispose();
}
