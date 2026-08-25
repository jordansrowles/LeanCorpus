using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Abstractions.Serialisation;

namespace Rowles.LeanCorpus.Server.Abstractions.Tests;

[Trait("Area", "Server")]
public sealed class SerialisationTests
{
    public static TheoryData<QueryDefinition, Type> CommunityQueries => new()
    {
        { new QueryStringDefinition("text"), typeof(QueryStringDefinition) },
        { new TermQueryDefinition("title", "text"), typeof(TermQueryDefinition) },
        { new BooleanQueryDefinition(Must: [new TermQueryDefinition("title", "text")]), typeof(BooleanQueryDefinition) },
        { new PhraseQueryDefinition("title", ["one", "two"]), typeof(PhraseQueryDefinition) },
        { new PrefixQueryDefinition("title", "pre"), typeof(PrefixQueryDefinition) },
        { new WildcardQueryDefinition("title", "t*"), typeof(WildcardQueryDefinition) },
        { new RegexpQueryDefinition("title", "t.*"), typeof(RegexpQueryDefinition) },
        { new SpanNearQueryDefinition([new TermQueryDefinition("title", "one"), new TermQueryDefinition("title", "two")], 1, true), typeof(SpanNearQueryDefinition) },
        { new VectorQueryDefinition("embedding", [1f, 0f], 5), typeof(VectorQueryDefinition) }
    };

    [Theory]
    [MemberData(nameof(CommunityQueries))]
    public void EveryCommunityQueryDiscriminatorRoundTrips(QueryDefinition query, Type expectedType)
    {
        SearchRequest request = new(query);
        string json = JsonSerializer.Serialize(request, ServerJsonSerialiserContext.Default.SearchRequest);
        SearchRequest? roundTrip = JsonSerializer.Deserialize(json, ServerJsonSerialiserContext.Default.SearchRequest);

        Assert.NotNull(roundTrip);
        Assert.IsType(expectedType, roundTrip.Query);
    }
}
