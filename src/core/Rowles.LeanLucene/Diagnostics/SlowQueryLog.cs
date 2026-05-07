using System.Text.Json;
using Rowles.LeanLucene.Serialization;

namespace Rowles.LeanLucene.Diagnostics;

/// <summary>
/// Logs individual queries that exceed a configurable latency threshold.
/// Output is one JSON object per line (JSON Lines format).
/// </summary>
public sealed class SlowQueryLog : IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private readonly Lock _lock = new();

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
    }

    /// <summary>Creates a slow query log that appends to a file.</summary>
    public static SlowQueryLog ToFile(double thresholdMs, string filePath)
    {
        var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
        var writer = new StreamWriter(stream) { AutoFlush = true };
        return new SlowQueryLog(thresholdMs, writer, ownsWriter: true);
    }

    /// <summary>
    /// Records a query execution. If elapsed exceeds the threshold, writes a JSON line to the output.
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

        string json = JsonSerializer.Serialize(entry, LeanLuceneJsonContext.Default.SlowQueryEntry);

        lock (_lock)
        {
            _writer.WriteLine(json);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsWriter) _writer.Dispose();
    }
}
