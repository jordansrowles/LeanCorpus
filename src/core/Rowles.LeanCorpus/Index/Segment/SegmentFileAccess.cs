using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Resolves direct or compound segment members without exposing storage details to codecs.</summary>
internal sealed class SegmentFileAccess : IDisposable
{
    private readonly MMapDirectory _directory;
    private readonly string _segmentId;
    private readonly CompoundFileReader? _compound;

    private SegmentFileAccess(MMapDirectory directory, string segmentId, CompoundFileReader? compound)
    {
        _directory = directory;
        _segmentId = segmentId;
        _compound = compound;
    }

    internal static SegmentFileAccess Open(MMapDirectory directory, SegmentInfo info)
    {
        if (!info.IsCompoundFile)
            return new SegmentFileAccess(directory, info.SegmentId, null);

        string cfsName = info.SegmentId + ".cfs";
        if (!directory.FileExists(cfsName))
            throw new FileNotFoundException($"Compound segment file is missing: '{cfsName}'.", cfsName);
        return new SegmentFileAccess(directory, info.SegmentId, CompoundFileReader.Open(directory, cfsName));
    }

    internal bool IsCompound => _compound is not null;

    internal string Name(string extension) => _segmentId + extension;

    internal bool Exists(string extension)
    {
        string name = Name(extension);
        return _compound?.HasFile(name) ?? _directory.FileExists(name);
    }

    internal IndexInput OpenInput(string extension)
    {
        string name = Name(extension);
        return _compound?.OpenInput(_directory, name) ?? _directory.OpenInput(name);
    }

    internal string DirectPath(string extension)
        => Path.Combine(_directory.DirectoryPath, Name(extension));

    public void Dispose() => _compound?.Dispose();
}
