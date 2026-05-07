using System.Text.Json;

namespace Rowles.LeanLucene.Index.Indexer;

internal static class CommitDeletionPolicy
{
    internal static bool TryParseCommitGeneration(string fileName, out int generation)
    {
        generation = 0;
        return fileName.StartsWith("segments_", StringComparison.Ordinal)
            && int.TryParse(fileName["segments_".Length..], out generation);
    }

    internal static bool TryParseStatsGeneration(string fileNameWithoutExtension, out int generation)
    {
        generation = 0;
        return fileNameWithoutExtension.StartsWith("stats_", StringComparison.Ordinal)
            && int.TryParse(fileNameWithoutExtension["stats_".Length..], out generation);
    }

    internal static bool ReferencesProtectedSegment(string commitFilePath, IReadOnlySet<string> protectedSegmentIds)
    {
        if (protectedSegmentIds.Count == 0 || !File.Exists(commitFilePath))
            return false;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(CommitFileFormat.ReadJson(commitFilePath)));
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("Segments", out var segments) ||
            segments.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var segment in segments.EnumerateArray())
        {
            var segmentId = segment.GetString();
            if (segmentId is not null && protectedSegmentIds.Contains(segmentId))
                return true;
        }

        return false;
    }
}
