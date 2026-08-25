using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>Provides logical segment files independently of their physical storage.</summary>
internal interface ISegmentFileSource : IDisposable
{
    /// <summary>Gets logical file names in stable ordinal order.</summary>
    IReadOnlyList<string> EnumerateFiles();

    /// <summary>Returns whether the logical file exists.</summary>
    bool FileExists(string fileName);

    /// <summary>Gets the logical file length in bytes.</summary>
    long GetFileLength(string fileName);

    /// <summary>Opens a bounded input for the logical file.</summary>
    IndexInput OpenInput(string fileName);
}
