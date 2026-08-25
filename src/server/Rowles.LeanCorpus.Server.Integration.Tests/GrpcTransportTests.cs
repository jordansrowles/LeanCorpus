using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using GrpcContracts = Rowles.LeanCorpus.Server.Grpc.Contracts;

namespace Rowles.LeanCorpus.Server.Integration.Tests;

[Trait("Area", "Server")]
public sealed class GrpcTransportTests
{
    [Fact]
    public async Task TypedGrpcServicesExecuteTheCommunityScenario()
    {
        await using ServerHostScope host = await ServerHostScope.StartAsync(HttpProtocols.Http2);
        using var channel = host.CreateGrpcChannel();
        GrpcContracts.HealthService.HealthServiceClient health = new(channel);
        GrpcContracts.IndexService.IndexServiceClient indices = new(channel);
        GrpcContracts.SearchService.SearchServiceClient search = new(channel);
        GrpcContracts.InspectionService.InspectionServiceClient inspection = new(channel);

        GrpcContracts.HealthResponse healthResponse = await health.GetHealthAsync(new Empty());
        Assert.True(healthResponse.IsHealthy);
        Assert.Equal("1", healthResponse.Metadata.ApiVersion);

        GrpcContracts.CreateIndexRequest create = new()
        {
            IndexName = "books",
            Schema = Struct.Parser.ParseJson("""{"fields":[{"name":"title","type":0,"indexed":true,"stored":true,"multiValued":false,"analyser":"standard"},{"name":"isbn","type":1,"indexed":true,"stored":true,"multiValued":false}],"analysis":{}}"""),
            Settings = Struct.Parser.ParseJson("""{"defaultField":"title"}"""),
            ShardCount = 1,
            ReplicaCount = 0
        };
        GrpcContracts.IndexMutationResponse created = await indices.CreateIndexAsync(create);
        Assert.Null(created.Failure);
        Assert.Equal("books", created.Index.IndexName);

        GrpcContracts.BulkDocumentsRequest bulk = new() { IndexName = "books", Refresh = true, IdempotencyKey = "grpc-write" };
        bulk.Operations.Add(new GrpcContracts.BulkDocumentOperation
        {
            Kind = "Index",
            DocumentId = "one",
            Document = Struct.Parser.ParseJson("""{"title":"Typed transport","isbn":"grpc-1"}""")
        });
        GrpcContracts.BulkDocumentsResponse indexed = await indices.BulkDocumentsAsync(bulk);
        Assert.Null(indexed.Failure);
        Assert.True(indexed.Items[0].Accepted);

        GrpcContracts.SearchRequest request = new()
        {
            IndexName = "books",
            Query = Struct.Parser.ParseJson("""{"kind":"term","field":"isbn","value":"grpc-1"}"""),
            Size = 10,
            IncludeDocuments = true
        };
        GrpcContracts.SearchResponse result = await search.SearchAsync(request);
        Assert.Null(result.Failure);
        Assert.Equal("one", Assert.Single(result.Hits).DocumentId);

        GrpcContracts.ExplainResponse explanation = await search.ExplainAsync(new GrpcContracts.ExplainRequest
        {
            IndexName = "books",
            DocumentId = "one",
            Query = request.Query
        });
        Assert.Null(explanation.Failure);
        Assert.True(explanation.Explanation.IsMatch);

        Assert.Null((await indices.GetSchemaAsync(new GrpcContracts.GetIndexRequest { IndexName = "books" })).Failure);
        Assert.Null((await indices.GetStatisticsAsync(new GrpcContracts.GetIndexRequest { IndexName = "books" })).Failure);
        GrpcContracts.InspectionResponse inspected = await inspection.InspectAsync(new GrpcContracts.InspectionRequest { IndexName = "books", Resource = "Documents", Limit = 10 });
        Assert.Null(inspected.Failure);

        GrpcContracts.DeleteIndexResponse rejected = await indices.DeleteIndexAsync(new GrpcContracts.DeleteIndexRequest { IndexName = "books" });
        Assert.Equal("confirmation_required", rejected.Failure.Code);
        GrpcContracts.DeleteIndexResponse deleted = await indices.DeleteIndexAsync(new GrpcContracts.DeleteIndexRequest
        {
            IndexName = "books",
            ConfirmationToken = ConfirmationTokens.Create("delete-index", "books")
        });
        Assert.Null(deleted.Failure);
        Assert.True(deleted.Deleted);
    }

    [Fact]
    public async Task GrpcHonoursCallerCancellation()
    {
        await using ServerHostScope host = await ServerHostScope.StartAsync(HttpProtocols.Http2);
        using var channel = host.CreateGrpcChannel();
        GrpcContracts.HealthService.HealthServiceClient health = new(channel);
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        RpcException exception = await Assert.ThrowsAsync<RpcException>(async () =>
            await health.GetHealthAsync(new Empty(), cancellationToken: cancelled.Token));
        Assert.Equal(StatusCode.Cancelled, exception.StatusCode);
    }
}
