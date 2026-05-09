using System.Text.Json;
using Rowles.LeanLucene.Serialization;

namespace Rowles.LeanLucene.Search.Scoring;

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

    /// <summary>Initialises a new <see cref="IndexStats"/> with pre-computed corpus statistics.</summary>
    /// <param name="totalDocCount">Total number of documents across all segments, including deleted.</param>
    /// <param name="liveDocCount">Total number of live (non-deleted) documents across all segments.</param>
    /// <param name="avgFieldLengths">Per-field average document length in token count.</param>
    /// <param name="fieldDocCounts">Per-field document frequency.</param>
    public IndexStats(
        int totalDocCount,
        int liveDocCount,
        Dictionary<string, float> avgFieldLengths,
        Dictionary<string, int> fieldDocCounts)
    {
        TotalDocCount = totalDocCount;
        LiveDocCount = liveDocCount;
        _avgFieldLengths = avgFieldLengths;
        _fieldDocCounts = fieldDocCounts;
    }

    /// <summary>Returns the average field length for a given field, defaulting to 1.0f.</summary>
    public float GetAvgFieldLength(string field)
        => _avgFieldLengths.GetValueOrDefault(field, 1.0f);

    /// <summary>Returns the number of documents containing the given field.</summary>
    public int GetFieldDocCount(string field)
        => _fieldDocCounts.GetValueOrDefault(field, 0);

    /// <summary>Returns a copy of the average field lengths dictionary (for serialisation).</summary>
    internal Dictionary<string, float> GetAvgFieldLengths()
        => new(_avgFieldLengths, StringComparer.Ordinal);

    /// <summary>Returns a copy of the field doc counts dictionary (for serialisation).</summary>
    internal Dictionary<string, int> GetFieldDocCounts()
        => new(_fieldDocCounts, StringComparer.Ordinal);

    /// <summary>An empty stats instance used for new or unreadable indexes.</summary>
    public static IndexStats Empty => new(0, 0, [], []);

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
        };
        var json = JsonSerializer.Serialize(dto, LeanLuceneJsonContext.Default.IndexStatsDto);

        // Use a unique tmp suffix so concurrent or rapidly-repeated writes
        // never collide on the same temp path. File.Move is atomic on a single
        // filesystem; if the source vanishes between WriteAllText and Move
        // (test harness cleanup, antivirus, etc), tolerate it provided the
        // destination ended up with content.
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, json);
        try
        {
            File.Move(tmp, path, overwrite: true);
        }
        catch (UnauthorizedAccessException) when (File.Exists(path))
        {
            // Stats are an optimisation sidecar. If a Windows reader has the old file
            // open, keep the previous stats rather than failing an otherwise durable commit.
        }
        catch (IOException) when (File.Exists(path))
        {
            // Same sidecar rule as above: the next commit will publish fresh stats.
        }
        catch (FileNotFoundException) when (File.Exists(path))
        {
            // The tmp was consumed by a concurrent move (shouldn't happen with
            // unique names, but defend against environmental interference).
            // The destination exists — accept that as success.
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try { File.Delete(tmp); } catch { /* best-effort cleanup */ }
            }
        }
    }

    /// <summary>
    /// Tries to load persisted <see cref="IndexStats"/> from the given path.
    /// Returns null if the file does not exist or is corrupt.
    /// </summary>
    public static IndexStats? TryLoadFrom(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, LeanLuceneJsonContext.Default.IndexStatsDto);
            if (dto is null) return null;
            return new IndexStats(
                dto.TotalDocCount,
                dto.LiveDocCount,
                dto.AvgFieldLengths ?? new(StringComparer.Ordinal),
                dto.FieldDocCounts ?? new(StringComparer.Ordinal));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Builds the canonical stats file path for a given commit generation.</summary>
    public static string GetStatsPath(string directoryPath, int generation)
        => Path.Combine(directoryPath, $"stats_{generation}.json");
}
