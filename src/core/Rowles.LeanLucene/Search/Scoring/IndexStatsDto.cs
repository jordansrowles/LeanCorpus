using System.Text.Json.Serialization;

namespace Rowles.LeanLucene.Search.Scoring;

internal sealed class IndexStatsDto
{
    [JsonPropertyName("totalDocCount")]
    public int TotalDocCount { get; set; }

    [JsonPropertyName("liveDocCount")]
    public int LiveDocCount { get; set; }

    [JsonPropertyName("avgFieldLengths")]
    public Dictionary<string, float>? AvgFieldLengths { get; set; }

    [JsonPropertyName("fieldDocCounts")]
    public Dictionary<string, int>? FieldDocCounts { get; set; }
}
