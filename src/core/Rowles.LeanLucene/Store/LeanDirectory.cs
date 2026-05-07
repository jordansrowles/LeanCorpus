namespace Rowles.LeanLucene.Store;

/// <summary>
/// Abstract base for storage operations. Named LeanDirectory to
/// avoid collision with <see cref="System.IO.Directory"/>.
/// </summary>
public abstract class LeanDirectory
{
    /// <summary>Directory path backing this store.</summary>
    public abstract string DirectoryPath { get; }

    /// <summary>Creates a new file for writing.</summary>
    public abstract IndexOutput CreateOutput(string fileName);

    /// <summary>Opens an existing file for reading via memory-mapped I/O.</summary>
    public abstract IndexInput OpenInput(string fileName);

    /// <summary>Deletes the specified file from the directory.</summary>
    public abstract void DeleteFile(string fileName);

    /// <summary>Returns whether the specified file exists.</summary>
    public abstract bool FileExists(string fileName);

    /// <summary>Lists all files in the directory.</summary>
    public abstract string[] ListAll();
}
