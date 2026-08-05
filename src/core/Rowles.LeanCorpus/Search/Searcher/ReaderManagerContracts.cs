namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>Provides the reader captured by a <see cref="ReaderLease{TReader}"/>.</summary>
public struct ReaderLease<TReader> : IDisposable
    where TReader : class, IDisposable
{
    private Action? _release;

    internal ReaderLease(TReader reader, Action release)
    {
        Reader = reader;
        _release = release;
    }

    /// <summary>Gets the immutable reader view held by this lease.</summary>
    public TReader Reader { get; }

    /// <summary>Releases the lease and allows the reader to be retired.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

/// <summary>Reports the state of a generic reader manager.</summary>
public sealed record ReaderManagerDiagnostics(
    int ActiveReaders,
    int ActiveLeases,
    long Refreshes,
    long RefreshFailures,
    long DisposedReaders);

/// <summary>Reports a failed generic reader refresh.</summary>
public sealed class ReaderRefreshFailedEventArgs : EventArgs
{
    internal ReaderRefreshFailedEventArgs(Exception exception, long consecutiveFailures)
    {
        Exception = exception;
        ConsecutiveFailures = consecutiveFailures;
    }

    /// <summary>Gets the exception raised while opening the replacement reader.</summary>
    public Exception Exception { get; }

    /// <summary>Gets the number of consecutive refresh failures.</summary>
    public long ConsecutiveFailures { get; }
}
