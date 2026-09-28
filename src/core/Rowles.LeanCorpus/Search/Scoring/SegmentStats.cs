using System.Text.Json;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

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
        Func<int, bool>? isLive,
        IReadOnlyDictionary<string, int[]> fieldLengths,
        IReadOnlyDictionary<string, RoaringBitmap>? fieldPresence = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalDocCount);
        ArgumentNullException.ThrowIfNull(fieldLengths);

        int liveDocCount = 0;
        for (int docId = 0; docId < totalDocCount; docId++)
        {
            if (isLive?.Invoke(docId) ?? true)
                liveDocCount++;
        }

        var fieldLengthSums = new Dictionary<string, long>(StringComparer.Ordinal);
        var fieldDocCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var (field, lengths) in fieldLengths)
        {
            if (lengths.Length < totalDocCount)
                throw new InvalidDataException(
                    $"Field '{field}' has {lengths.Length} lengths for a segment with {totalDocCount} documents.");

            long sum = 0;
            int count = 0;
            RoaringBitmap? presence = null;
            fieldPresence?.TryGetValue(field, out presence);
            for (int docId = 0; docId < totalDocCount; docId++)
            {
                int length = lengths[docId];
                if (length < 0)
                    throw new InvalidDataException($"Field '{field}' has a negative length at document {docId}.");
                if (!(isLive?.Invoke(docId) ?? true))
                    continue;

                bool hasIndexedTerms = presence?.Contains(docId) ?? length > 0;
                if (!hasIndexedTerms)
                    continue;

                sum += length;
                count++;
            }

            fieldLengthSums[field] = sum;
            fieldDocCounts[field] = count;
        }

        return new SegmentStats(totalDocCount, liveDocCount, fieldLengthSums, fieldDocCounts);
    }

    internal static SegmentStats FromSegmentReader(SegmentReader reader, Func<int, bool>? isLive = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        var fieldLengths = new Dictionary<string, int[]>(StringComparer.Ordinal);
        foreach (string field in reader.Info.FieldNames)
        {
            if (reader.TryGetFieldLengths(field, out var lengths))
                fieldLengths[field] = lengths;
        }

        var fieldPresence = new Dictionary<string, RoaringBitmap>(StringComparer.Ordinal);
        foreach (string field in reader.Info.FieldNames)
        {
            bool hasExactLengths = reader.FileExists(".fln") && fieldLengths.ContainsKey(field);
            if (hasExactLengths)
                continue;

            var docsWithTerms = new RoaringBitmap();
            foreach (var (term, _) in reader.GetAllTermsForField(field + "\0"))
            {
                using var postings = reader.GetPostingsEnum(term);
                while (postings.MoveNext())
                    docsWithTerms.Add(postings.DocId);
            }

            fieldPresence[field] = docsWithTerms;
            if (docsWithTerms.Cardinality > 0 && !fieldLengths.ContainsKey(field))
            {
                var fallbackLengths = new int[reader.MaxDoc];
                Array.Fill(fallbackLengths, 1);
                fieldLengths[field] = fallbackLengths;
            }
        }

        Func<int, bool> livePredicate = isLive ?? reader.IsLive;
        return FromFieldLengths(reader.MaxDoc, livePredicate, fieldLengths, fieldPresence);
    }

    internal void WriteTo(string path)
    {
        var dto = new SegmentStatsDto
        {
            StatisticsVersion = IndexStats.CurrentStatisticsVersion,
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
        if (!FileOpenRetry.FileExists(path))
            return null;

        try
        {
            var json = FileOpenRetry.ReadAllText(path);
            var dto = JsonSerializer.Deserialize(json, LeanCorpusJsonContext.Default.SegmentStatsDto);
            if (dto is null || dto.StatisticsVersion != IndexStats.CurrentStatisticsVersion ||
                dto.TotalDocCount < 0 || dto.LiveDocCount < 0 || dto.LiveDocCount > dto.TotalDocCount ||
                dto.FieldLengthSums is null || dto.FieldDocCounts is null ||
                dto.FieldLengthSums.Count != dto.FieldDocCounts.Count ||
                dto.FieldLengthSums.Keys.Any(field => !dto.FieldDocCounts.ContainsKey(field)) ||
                dto.FieldLengthSums.Any(static pair => pair.Value < 0) ||
                dto.FieldDocCounts.Any(pair => pair.Value < 0 || pair.Value > dto.LiveDocCount))
                return null;

            return new SegmentStats(
                dto.TotalDocCount,
                dto.LiveDocCount,
                dto.FieldLengthSums,
                dto.FieldDocCounts);
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

    internal static string GetStatsPath(string directoryPath, string segmentId, int? deletionGeneration)
        => deletionGeneration is int generation
            ? Path.Combine(directoryPath, $"{segmentId}_gen_{generation}.stats.json")
            : GetStatsPath(directoryPath, segmentId);

    internal static string GetStatsPathForRead(string directoryPath, string segmentId, int? deletionGeneration)
    {
        if (deletionGeneration is not int generation)
            return GetStatsPath(directoryPath, segmentId);

        string generationPath = GetStatsPath(directoryPath, segmentId, generation);
        return FileOpenRetry.FileExists(generationPath)
            ? generationPath
            : GetStatsPath(directoryPath, segmentId);
    }
}
