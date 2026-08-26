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

        GrpcContracts.BulkDocumentsRequest bulk = new()
        {
            IndexName = "books",
            Refresh = true,
            IdempotencyKey = "grpc-write",
            Durability = "LocalFsync"
        };
        bulk.Operations.Add(new GrpcContracts.BulkDocumentOperation
        {
            Kind = "Index",
            DocumentId = "one",
            Document = Struct.Parser.ParseJson("""{"title":"Typed transport","isbn":"grpc-1"}""")
        });
        GrpcContracts.BulkDocumentsResponse indexed = await indices.BulkDocumentsAsync(bulk);
        Assert.Null(indexed.Failure);
        Assert.True(indexed.Items[0].Accepted);
        Assert.Equal(created.Index.IndexId, indexed.WriteToken.IndexId);

        GrpcContracts.HealthResponse indexedHealth = await health.GetHealthAsync(new Empty());
        Assert.True(indexedHealth.IsHealthy);
        Assert.Equal("healthy", indexedHealth.Status);
        GrpcContracts.IndexHealthSummary indexedHealthIndex = Assert.Single(indexedHealth.Indices);
        Assert.Equal("books", indexedHealthIndex.IndexName);
        Assert.True(indexedHealthIndex.VisibleGeneration > 0);
        Assert.Equal(indexedHealthIndex.VisibleGeneration, indexedHealthIndex.DurableGeneration);

        GrpcContracts.SearchRequest request = new()
        {
            IndexName = "books",
            Query = Struct.Parser.ParseJson("""{"kind":"term","field":"isbn","value":"grpc-1"}"""),
            Size = 10,
            IncludeDocuments = true,
            Consistency = "ReadYourWrites",
            ReadToken = indexed.WriteToken
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

        GrpcContracts.ExplainResponse unsupportedExplanation = await search.ExplainAsync(new GrpcContracts.ExplainRequest
        {
            IndexName = "books",
            DocumentId = "one",
            Query = Struct.Parser.ParseJson("""{"kind":"phrase","field":"title","terms":["typed","transport"]}""")
        });
        Assert.Equal("explain_not_supported", unsupportedExplanation.Failure?.Code);

        GrpcContracts.BulkDocumentsResponse unsupportedDurability = await indices.BulkDocumentsAsync(new GrpcContracts.BulkDocumentsRequest
        {
            IndexName = "books",
            Durability = "Quorum",
            Operations = { new GrpcContracts.BulkDocumentOperation
            {
                Kind = "Index",
                DocumentId = "two",
                Document = Struct.Parser.ParseJson("""{"title":"Second","isbn":"grpc-2"}""")
            }}
        });
        Assert.Equal("durability_not_supported", unsupportedDurability.Failure?.Code);

        GrpcContracts.SearchResponse unsupportedConsistency = await search.SearchAsync(new GrpcContracts.SearchRequest
        {
            IndexName = "books",
            Query = request.Query,
            Consistency = "Replica"
        });
        Assert.Equal("consistency_unavailable", unsupportedConsistency.Failure?.Code);

        GrpcContracts.InspectionResponse unsupportedInspection = await inspection.InspectAsync(new GrpcContracts.InspectionRequest
        {
            IndexName = "books",
            Resource = "Terms",
            Limit = 10
        });
        Assert.Equal("inspection_not_supported", unsupportedInspection.Failure?.Code);

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
