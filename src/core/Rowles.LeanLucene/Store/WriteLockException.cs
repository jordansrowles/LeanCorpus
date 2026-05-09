namespace Rowles.LeanLucene.Store;

/// <summary>
/// Thrown when an <see cref="Rowles.LeanLucene.Index.Indexer.IndexWriter"/> cannot acquire the
/// write lock because another writer already holds it for the same directory.
/// </summary>
public sealed class WriteLockException : IOException
{
    /// <summary>
    /// Initialises a new <see cref="WriteLockException"/> for the specified directory.
    /// </summary>
    /// <param name="directory">The directory path that is already locked.</param>
    public WriteLockException(string directory)
        : base($"Index is already locked for write access: '{directory}'. Ensure no other IndexWriter is open on the same directory.") { }
}
