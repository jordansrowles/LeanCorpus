using System.Text.Json.Serialization;

namespace Rowles.LeanLucene.Search.Scoring;

internal sealed class SegmentStatsDto
{
    [JsonPropertyName("totalDocCount")]
    public int TotalDocCount { get; set; }

    [JsonPropertyName("liveDocCount")]
    public int LiveDocCount { get; set; }

    [JsonPropertyName("fieldLengthSums")]
    public Dictionary<string, long>? FieldLengthSums { get; set; }

    [JsonPropertyName("fieldDocCounts")]
    public Dictionary<string, int>? FieldDocCounts { get; set; }
}
