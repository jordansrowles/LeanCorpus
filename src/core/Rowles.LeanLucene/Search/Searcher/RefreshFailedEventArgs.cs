namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Carries the exception and consecutive-failure count for a failed
/// <see cref="SearcherManager"/> refresh.
/// </summary>
public sealed class RefreshFailedEventArgs : EventArgs
{
    /// <summary>The exception that caused the refresh to fail.</summary>
    public Exception Error { get; }

    /// <summary>The number of consecutive failed refreshes including this one.</summary>
    public long ConsecutiveFailures { get; }

    /// <summary>The UTC time at which the failure was recorded.</summary>
    public DateTime At { get; } = DateTime.UtcNow;

    /// <summary>Creates a new <see cref="RefreshFailedEventArgs"/>.</summary>
    public RefreshFailedEventArgs(Exception error, long consecutiveFailures)
    {
        Error = error;
        ConsecutiveFailures = consecutiveFailures;
    }
}
