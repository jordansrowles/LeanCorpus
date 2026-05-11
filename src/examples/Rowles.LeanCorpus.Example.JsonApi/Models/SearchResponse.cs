namespace Rowles.LeanCorpus.Example.JsonApi.Models;

public sealed class CollectionInfo
{
    public required string Name { get; init; }
    public int DocCount { get; init; }
}

public sealed class IndexResponse
{
    public required int Indexed { get; init; }
}

public sealed class DeleteResponse
{
    public required int Deleted { get; init; }
}

public sealed class SearchHit
{
    public required float Score { get; init; }
    public required Dictionary<string, object?> Fields { get; init; }
}

public sealed class SearchResponse
{
    public required int TotalHits { get; init; }
    public required List<SearchHit> Hits { get; init; }
    public required List<string> Suggestions { get; init; }
}
