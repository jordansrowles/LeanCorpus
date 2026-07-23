namespace Rowles.LeanCorpus.Store;

/// <summary>
/// Primary directory implementation using memory-mapped files for reads
/// and buffered file streams for writes.
/// </summary>
public sealed class MMapDirectory : LeanDirectory, IDisposable
{
    private readonly List<WeakReference<IndexInput>> _trackedInputs = [];
    private readonly Lock _trackLock = new();
    private readonly FileLifetimeRegistry.DirectoryState _fileLifetimes;
    private volatile bool _disposed;

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
        _fileLifetimes = FileLifetimeRegistry.ForDirectory(path);
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
        fileName = ValidateFileName(fileName);
        var lease = _fileLifetimes.Acquire(fileName);
        try
        {
            var input = new IndexInput(Path.Combine(DirectoryPath, fileName));
            input.SetOnDisposed(_ => lease.Dispose());
            TrackInput(input);
            return input;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public override void DeleteFile(string fileName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _fileLifetimes.Delete(ValidateFileName(fileName));
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

    internal FileSnapshotLease AcquireSnapshot(IReadOnlyCollection<string> fileNames)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _fileLifetimes.AcquireSnapshot(fileNames);
    }

    internal FileSnapshotLease AcquireSnapshot(
        Func<string, bool> includeFile,
        out string[] inventory)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _fileLifetimes.AcquireSnapshot(includeFile, out inventory);
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
