using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Retains encoded leaf data in memory or in a temporary file.</summary>
internal sealed class PackedBkdLeafDataStore : IDisposable
{
    private readonly string _path;
    private readonly Stream _stream;
    private readonly long[] _offsets;
    private readonly PackedBkdBuildMemoryTracker _tracker;
    private readonly long _offsetBytes;
    private int _nextLeaf;
    private bool _disposed;

    internal PackedBkdLeafDataStore(
        string directory,
        int leafCount,
        PackedBkdBuildMemoryTracker tracker)
    {
        FileOpenRetry.CreateDirectory(directory);
        _path = Path.Combine(directory, $"packed-bkd-{Guid.NewGuid():N}.leaf");
        _stream = FileOpenRetry.Open(
            _path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            64 * 1024,
            FileOptions.SequentialScan);
        _tracker = tracker;
        _offsetBytes = checked((long)(leafCount + 1) * sizeof(long));
        bool offsetsReserved = false;
        try
        {
            tracker.Reserve(_offsetBytes);
            offsetsReserved = true;
            _offsets = new long[checked(leafCount + 1)];
        }
        catch
        {
            _stream.Dispose();
            if (offsetsReserved)
                tracker.Release(_offsetBytes);
            DeleteTemporaryFile();
            throw;
        }
    }

    internal long[] Offsets
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_nextLeaf != _offsets.Length - 1)
                throw new InvalidOperationException("Packed BKD leaf data is incomplete.");
            return _offsets;
        }
    }

    internal void Write(int index, ReadOnlySpan<byte> leaf)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (index != _nextLeaf)
            throw new InvalidOperationException("Packed BKD leaves must be produced in leaf order.");
        _offsets[index] = _stream.Position;
        _stream.Write(leaf);
        _nextLeaf++;
        _offsets[_nextLeaf] = _stream.Position;
    }

    internal void WriteTo(CodecBodyOutput output)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_nextLeaf != _offsets.Length - 1)
            throw new InvalidOperationException("Packed BKD leaf data is incomplete.");
        _stream.Position = 0;
        using Stream destination = output.AsStream();
        _stream.CopyTo(destination);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stream.Dispose();
        _tracker.Release(_offsetBytes);
        DeleteTemporaryFile();
    }

    private void DeleteTemporaryFile()
    {
        try
        {
            FileOpenRetry.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "packed BKD leaf cleanup");
        }
    }
}
