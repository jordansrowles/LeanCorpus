using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Immutable corpus-wide statistics computed once at <see cref="IndexSearcher"/>
/// construction. Shared across all segment readers so BM25 scores are
/// comparable across segment boundaries.
/// </summary>
public sealed class IndexStats
{
    /// <summary>Total number of documents across all segments (including deleted).</summary>
    public int TotalDocCount { get; }

    /// <summary>Total number of live (non-deleted) documents across all segments.</summary>
    public int LiveDocCount { get; }

    /// <summary>Per-field average document length (in token count).</summary>
    private readonly Dictionary<string, float> _avgFieldLengths;

    /// <summary>Per-field total document frequency (number of docs containing the field).</summary>
    private readonly Dictionary<string, int> _fieldDocCounts;

    /// <summary>Per-field sum of all token counts across all documents (total terms in collection).</summary>
    private readonly Dictionary<string, long> _fieldLengthSums;

    /// <summary>Initialises a new <see cref="IndexStats"/> with pre-computed corpus statistics.</summary>
    /// <param name="totalDocCount">Total number of documents across all segments, including deleted.</param>
    /// <param name="liveDocCount">Total number of live (non-deleted) documents across all segments.</param>
    /// <param name="avgFieldLengths">Per-field average document length in token count.</param>
    /// <param name="fieldDocCounts">Per-field document frequency.</param>
    /// <param name="fieldLengthSums">Per-field sum of all token counts across all documents.</param>
    public IndexStats(
        int totalDocCount,
        int liveDocCount,
        Dictionary<string, float> avgFieldLengths,
        Dictionary<string, int> fieldDocCounts,
        Dictionary<string, long> fieldLengthSums)
    {
        TotalDocCount = totalDocCount;
        LiveDocCount = liveDocCount;
        _avgFieldLengths = avgFieldLengths;
        _fieldDocCounts = fieldDocCounts;
        _fieldLengthSums = fieldLengthSums;
    }

    /// <summary>Returns the average field length for a given field, defaulting to 1.0f.</summary>
    public float GetAvgFieldLength(string field)
        => _avgFieldLengths.GetValueOrDefault(field, 1.0f);

    /// <summary>Returns the number of documents containing the given field.</summary>
    public int GetFieldDocCount(string field)
        => _fieldDocCounts.GetValueOrDefault(field, 0);

    /// <summary>Returns the total number of tokens in the collection for a given field, defaulting to 0.</summary>
    public long GetFieldLengthSum(string field)
        => _fieldLengthSums.GetValueOrDefault(field, 0L);

    /// <summary>Returns a copy of the average field lengths dictionary (for serialisation).</summary>
    internal Dictionary<string, float> GetAvgFieldLengths()
        => new(_avgFieldLengths, StringComparer.Ordinal);

    /// <summary>Returns a copy of the field doc counts dictionary (for serialisation).</summary>
    internal Dictionary<string, int> GetFieldDocCounts()
        => new(_fieldDocCounts, StringComparer.Ordinal);

    /// <summary>Returns a copy of the field length sums dictionary (for serialisation).</summary>
    internal Dictionary<string, long> GetFieldLengthSums()
        => new(_fieldLengthSums, StringComparer.Ordinal);

    /// <summary>An empty stats instance used for new or unreadable indexes.</summary>
    public static IndexStats Empty => new(0, 0, [], [], []);

    // --- Persistence ---

    /// <summary>
    /// Serialises this <see cref="IndexStats"/> to a JSON file at the given path.
    /// Uses write-temp-then-rename for atomicity.
    /// </summary>
    public void WriteTo(string path)
    {
        var dto = new IndexStatsDto
        {
            TotalDocCount = TotalDocCount,
            LiveDocCount = LiveDocCount,
            AvgFieldLengths = _avgFieldLengths,
            FieldDocCounts = _fieldDocCounts,
            FieldLengthSums = _fieldLengthSums,
        };
        var json = JsonSerializer.Serialize(dto, LeanCorpusJsonContext.Default.IndexStatsDto);

        // Use a unique tmp suffix so concurrent or rapidly-repeated writes
        // never collide on the same temp path. File.Move is atomic on a single
        // filesystem; if the source vanishes between WriteAllText and Move
        // (test harness cleanup, antivirus, etc), tolerate it provided the
        // destination ended up with content.
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var fs = FileOpenRetry.Open(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var sw = FileOpenRetry.OpenTextWriter(fs, Encoding.UTF8))
            sw.Write(json);
        try
        {
            FileOpenRetry.Move(tmp, path, overwrite: true);
        }
        catch (UnauthorizedAccessException) when (FileOpenRetry.FileExists(path))
        {
            // Stats are an optimisation sidecar. If a Windows reader has the old file
            // open, keep the previous stats rather than failing an otherwise durable commit.
        }
        catch (FileNotFoundException) when (FileOpenRetry.FileExists(path))
        {
            // The tmp was consumed by a concurrent move (shouldn't happen with
            // unique names, but defend against environmental interference).
            // The destination exists; accept that as success.
        }
        catch (IOException) when (FileOpenRetry.FileExists(path))
        {
            // Existing stats file is locked (concurrent searcher read, antivirus, etc.).
            // Keep the previous stats; the next commit will publish fresh ones.
        }
        catch (IOException)
        {
            // File.Move failed even though the destination does not exist.
            // This can happen on Windows when a transient handle (antivirus,
            // background searcher refresh) briefly pins the temp file or the
            // target directory entry. Stats are a best-effort sidecar; the
            // commit file (segments_N) is already durable. IndexSearcher falls
            // back to ComputeStats() when TryLoadFrom returns null, so a
            // missing stats file is harmless.
        }
        finally
        {
            if (FileOpenRetry.FileExists(tmp))
            {
                try { FileOpenRetry.Delete(tmp); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "stats temp file delete"); }
            }
        }
    }

    /// <summary>
    /// Tries to load persisted <see cref="IndexStats"/> from the given path.
    /// Returns null if the file does not exist or is corrupt.
    /// </summary>
    public static IndexStats? TryLoadFrom(string path)
    {
        if (!FileOpenRetry.FileExists(path)) return null;
        try
        {
            var json = FileOpenRetry.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.IndexStatsDto);
            if (dto is null) return null;
            return new IndexStats(
                dto.TotalDocCount,
                dto.LiveDocCount,
                dto.AvgFieldLengths ?? new(StringComparer.Ordinal),
                dto.FieldDocCounts ?? new(StringComparer.Ordinal),
                dto.FieldLengthSums ?? new(StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Builds the canonical stats file path for a given commit generation.</summary>
    public static string GetStatsPath(string directoryPath, int generation)
        => Path.Combine(directoryPath, $"stats_{generation}.json");
}
