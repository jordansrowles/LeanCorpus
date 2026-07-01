using System.Text.Json;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Search.Scoring;

internal sealed class SegmentStats
{
    internal SegmentStats(
        int totalDocCount,
        int liveDocCount,
        Dictionary<string, long> fieldLengthSums,
        Dictionary<string, int> fieldDocCounts)
    {
        TotalDocCount = totalDocCount;
        LiveDocCount = liveDocCount;
        FieldLengthSums = fieldLengthSums;
        FieldDocCounts = fieldDocCounts;
    }

    internal int TotalDocCount { get; }

    internal int LiveDocCount { get; }

    internal Dictionary<string, long> FieldLengthSums { get; }

    internal Dictionary<string, int> FieldDocCounts { get; }

    internal static SegmentStats FromFieldLengths(
        int totalDocCount,
        int liveDocCount,
        IEnumerable<string> fieldNames,
        IReadOnlyDictionary<string, int[]> fieldLengths)
    {
        var fieldLengthSums = new Dictionary<string, long>(StringComparer.Ordinal);
        var fieldDocCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var field in fieldNames)
        {
            long sum = 0;
            if (fieldLengths.TryGetValue(field, out var lengths))
            {
                int count = Math.Min(liveDocCount, lengths.Length);
                for (int i = 0; i < count; i++)
                    sum += lengths[i];
            }
            else
            {
                sum = liveDocCount;
            }

            fieldLengthSums[field] = sum;
            fieldDocCounts[field] = liveDocCount;
        }

        return new SegmentStats(totalDocCount, liveDocCount, fieldLengthSums, fieldDocCounts);
    }

    internal void WriteTo(string path)
    {
        var dto = new SegmentStatsDto
        {
            TotalDocCount = TotalDocCount,
            LiveDocCount = LiveDocCount,
            FieldLengthSums = FieldLengthSums,
            FieldDocCounts = FieldDocCounts,
        };

        var json = JsonSerializer.Serialize(dto, LeanCorpusJsonContext.Default.SegmentStatsDto);
        IndexAtomicFileWriter.WriteText(path, json, durable: false);
    }

    internal static SegmentStats? TryLoadFrom(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var json = FileOpenRetry.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.SegmentStatsDto);
            if (dto is null)
                return null;

            return new SegmentStats(
                dto.TotalDocCount,
                dto.LiveDocCount,
                dto.FieldLengthSums ?? new Dictionary<string, long>(StringComparer.Ordinal),
                dto.FieldDocCounts ?? new Dictionary<string, int>(StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static string GetStatsPath(string directoryPath, string segmentId)
        => Path.Combine(directoryPath, $"{segmentId}.stats.json");
}
