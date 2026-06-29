using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Primary directory implementation using memory-mapped files for reads
/// and buffered file streams for writes.
/// </summary>
public sealed class MMapDirectory : LeanDirectory, IDisposable
{
    private readonly List<WeakReference<IndexInput>> _trackedInputs = [];
    private readonly Lock _trackLock = new();
    private volatile bool _disposed;

    // Reference-counted deferred deletion: Windows cannot delete a file while it is
    // memory-mapped (even with FILE_SHARE_DELETE on the section).  Track how many
    // IndexInput instances have a given file mapped, and defer File.Delete until
    // the last mapping is released.
    private readonly ConcurrentDictionary<string, int> _openFileCounts = new(2, 128, StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pendingDeletes = new(2, 128, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override string DirectoryPath { get; }

    /// <summary>
    /// Initialises a new <see cref="MMapDirectory"/> backed by the given file system path.
    /// Creates the directory if it does not already exist.
    /// </summary>
    /// <param name="path">The file system path for the index directory. Must not be null.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="path"/> is null.</exception>
    public MMapDirectory(string path)
    {
        DirectoryPath = path ?? throw new ArgumentNullException(nameof(path));

        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    /// <inheritdoc/>
    public override IndexOutput CreateOutput(string fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var filePath = Path.Combine(DirectoryPath, ValidateFileName(fileName));
        return new IndexOutput(filePath);
    }

    /// <inheritdoc/>
    public override IndexInput OpenInput(string fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var filePath = Path.Combine(DirectoryPath, ValidateFileName(fileName));
        var input = new IndexInput(filePath);
        input.SetOnDisposed(OnInputDisposed);
        _openFileCounts.AddOrUpdate(filePath, 1, (_, count) => count + 1);
        TrackInput(input);
        return input;
    }

    /// <inheritdoc/>
    public override void DeleteFile(string fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var filePath = Path.Combine(DirectoryPath, ValidateFileName(fileName));
        TryDeleteOrDefer(filePath);
    }

    /// <inheritdoc/>
    public override bool FileExists(string fileName)
    {
        var filePath = Path.Combine(DirectoryPath, ValidateFileName(fileName));
        return File.Exists(filePath);
    }

    /// <inheritdoc/>
    public override string[] ListAll()
    {
        return Directory.GetFiles(DirectoryPath)
            .Select(Path.GetFileName)
            .Where(name => name is not null)
            .ToArray()!;
    }

    /// <summary>
    /// Disposes this directory. Any tracked <see cref="IndexInput"/> instances that have
    /// not yet been disposed are closed. Callers should ensure all active readers are
    /// disposed before calling this method.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        lock (_trackLock)
        {
            foreach (var weakRef in _trackedInputs)
            {
                if (weakRef.TryGetTarget(out var input))
                    input.Dispose();
            }
            _trackedInputs.Clear();
        }
        // After all inputs are disposed, perform any remaining deferred deletions.
        foreach (var kvp in _pendingDeletes)
        {
            try { File.Delete(kvp.Key); }
            catch { /* best-effort on teardown */ }
        }
        _pendingDeletes.Clear();
        _openFileCounts.Clear();
    }

    private void TrackInput(IndexInput input)
    {
        lock (_trackLock)
        {
            // Prune dead references opportunistically to keep the list from growing unbounded.
            if (_trackedInputs.Count > 0 && _trackedInputs.Count % 64 == 0)
                _trackedInputs.RemoveAll(r => !r.TryGetTarget(out _));

            _trackedInputs.Add(new WeakReference<IndexInput>(input));
        }
    }

    /// <summary>
    /// Callback invoked when an <see cref="IndexInput"/> is disposed after releasing
    /// its memory-mapped file resources. Decrements the reference count for the file
    /// and, if it reaches zero and the file is pending deletion, removes it from disk.
    /// </summary>
    private void OnInputDisposed(IndexInput input)
    {
        var filePath = input.FilePath;
        if (filePath is null) return;

        int newCount = _openFileCounts.AddOrUpdate(filePath, 0, (_, count) => count - 1);
        if (newCount <= 0)
        {
            _openFileCounts.TryRemove(filePath, out _);
            if (_pendingDeletes.TryRemove(filePath, out _))
            {
                try { File.Delete(filePath); }
                catch { /* best-effort — will retry on next Dispose/teardown */ }
            }
        }
    }

    /// <summary>
    /// Attempts to delete the file at <paramref name="filePath"/>. If the file is
    /// still open (memory-mapped), the deletion is deferred until the last
    /// <see cref="IndexInput"/> mapping the file is disposed.
    /// </summary>
    private void TryDeleteOrDefer(string filePath)
    {
        // Fast path: file is not currently mapped.
        if (!_openFileCounts.ContainsKey(filePath))
        {
            try { File.Delete(filePath); }
            catch { /* best-effort */ }
            return;
        }

        // File is still mapped. Try to delete anyway (works on Linux, may fail on Windows).
        try { File.Delete(filePath); }
        catch
        {
            // Deletion blocked by memory-mapping — defer until last handle is released.
            _pendingDeletes.TryAdd(filePath, 0);
        }
    }

    private static string ValidateFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (Path.IsPathRooted(fileName) || fileName != Path.GetFileName(fileName))
            throw new ArgumentException("File name must not contain path components.", nameof(fileName));

        // Cross-platform: Path.GetFileName on POSIX treats backslash as a
        // regular character, so "..\\..\\foo" passes the check above. Reject
        // every separator and every traversal segment explicitly.
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
            throw new ArgumentException("File name must not contain path separators or traversal segments.", nameof(fileName));

        // Colon creates alternate data streams on Windows (e.g. "foo:bar"
        // writes to an ADS on file "foo" rather than creating "foo:bar").
        if (fileName.Contains(':'))
            throw new ArgumentException("File name must not contain a colon.", nameof(fileName));

        foreach (var c in fileName)
        {
            if (char.IsControl(c))
                throw new ArgumentException("File name must not contain control characters.", nameof(fileName));
        }

        return fileName;
    }
}
