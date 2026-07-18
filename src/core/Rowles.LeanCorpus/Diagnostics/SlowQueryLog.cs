using System.Text.Json;
using System.Threading.Channels;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// Logs individual queries that exceed a configurable latency threshold.
/// Output is one JSON object per line (JSON Lines format).
/// Writes are offloaded to a background channel consumer so slow disk I/O
/// never blocks the search hot path.
/// </summary>
public sealed class SlowQueryLog : IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private readonly Channel<SlowQueryEntry> _channel;
    private readonly Task _writeTask;
    private int _disposed;

    /// <summary>Minimum elapsed milliseconds before a query is logged.</summary>
    public double ThresholdMs { get; }

    /// <summary>Whether to include query explain output in log entries.</summary>
    public bool IncludeExplain { get; init; }

    /// <summary>Creates a slow query log that writes to the given <see cref="TextWriter"/>.</summary>
    public SlowQueryLog(double thresholdMs, TextWriter writer, bool ownsWriter = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(thresholdMs);
        ThresholdMs = thresholdMs;
        _writer = writer;
        _ownsWriter = ownsWriter;

        _channel = Channel.CreateBounded<SlowQueryEntry>(new BoundedChannelOptions(1024)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
        _writeTask = Task.Run(WriteLoop);
    }

    /// <summary>Creates a slow query log that appends to a file.</summary>
    public static SlowQueryLog ToFile(double thresholdMs, string filePath)
    {
        var writer = FileOpenRetry.OpenAppendText(filePath);
        return new SlowQueryLog(thresholdMs, writer, ownsWriter: true);
    }

    /// <summary>
    /// Records a query execution. If elapsed exceeds the threshold, enqueues
    /// a log entry for asynchronous writing. Never blocks on disk I/O.
    /// </summary>
    internal void MaybeLog(Search.Query query, TimeSpan elapsed, int totalHits)
    {
        double ms = elapsed.TotalMilliseconds;
        if (ms < ThresholdMs) return;

        var entry = new SlowQueryEntry
        {
            Timestamp = DateTime.UtcNow,
            QueryType = query.GetType().Name,
            Query = query.ToString() ?? query.GetType().Name,
            ElapsedMs = Math.Round(ms, 2),
            TotalHits = totalHits
        };

        _channel.Writer.TryWrite(entry);
    }

    private async Task WriteLoop()
    {
        while (await _channel.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (_channel.Reader.TryRead(out var entry))
            {
                string json = JsonSerializer.Serialize(entry, LeanCorpusJsonContext.Default.SlowQueryEntry);
                _writer.WriteLine(json);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _channel.Writer.Complete();
        _writeTask.GetAwaiter().GetResult();
        _writer.Flush();
        if (_ownsWriter) _writer.Dispose();
    }
}
