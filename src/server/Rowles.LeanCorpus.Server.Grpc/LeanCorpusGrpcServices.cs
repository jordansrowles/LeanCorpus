using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Abstractions.Services;
using GrpcContracts = Rowles.LeanCorpus.Server.Grpc.Contracts;
using ContractBulkRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents.BulkDocumentsRequest;
using ContractCreateRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.CreateIndexRequest;
using ContractDeleteRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.DeleteIndexRequest;
using ContractExplainRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.ExplainRequest;
using ContractInspectionRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection.InspectionRequest;
using ContractRefreshRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.RefreshIndexRequest;
using ContractSearchRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.SearchRequest;
using ContractUpdateRequest = Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.UpdateIndexSettingsRequest;

namespace Rowles.LeanCorpus.Server.Grpc;

/// <summary>Maps the version-one Community gRPC services.</summary>
public static class LeanCorpusGrpcEndpointRouteBuilderExtensions
{
    /// <summary>Registers every Community gRPC service against the shared Core contracts.</summary>
    public static IEndpointRouteBuilder MapLeanCorpusServerGrpc(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGrpcService<LeanCorpusSearchGrpcService>();
        endpoints.MapGrpcService<LeanCorpusIndexGrpcService>();
        endpoints.MapGrpcService<LeanCorpusInspectionGrpcService>();
        endpoints.MapGrpcService<LeanCorpusHealthGrpcService>();
        return endpoints;
    }
}

/// <summary>Implements search and explain over typed protobuf messages.</summary>
public sealed class LeanCorpusSearchGrpcService(ISearchService service) : GrpcContracts.SearchService.SearchServiceBase
{
    /// <inheritdoc />
    public override async Task<GrpcContracts.SearchResponse> Search(GrpcContracts.SearchRequest request, ServerCallContext context)
    {
        QueryDefinition query = GrpcContractMapper.FromStruct<QueryDefinition>(request.Query, "A query is required.");
        ContractSearchRequest contract = new(
            query,
            request.HasSize ? request.Size : 10,
            GrpcContractMapper.FromList(request.SearchAfter),
            request.Sort.Select(GrpcContractMapper.ToContract).ToArray(),
            request.Facets.Select(GrpcContractMapper.ToContract).ToArray(),
            GrpcContractMapper.ParseEnum<RequestedConsistency>(request.Consistency, RequestedConsistency.Local),
            request.HasIncludeDocuments ? request.IncludeDocuments : true,
            request.IncludeHighlights);
        ServiceResult<Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.SearchResponse> result =
            await service.SearchAsync(request.IndexName, contract, context.CancellationToken).ConfigureAwait(false);
        return GrpcContractMapper.ToGrpc(result);
    }

    /// <inheritdoc />
    public override async Task<GrpcContracts.ExplainResponse> Explain(GrpcContracts.ExplainRequest request, ServerCallContext context)
    {
        QueryDefinition query = GrpcContractMapper.FromStruct<QueryDefinition>(request.Query, "A query is required.");
        ServiceResult<Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.ExplainResponse> result =
            await service.ExplainAsync(request.IndexName, new ContractExplainRequest(request.DocumentId, query), context.CancellationToken).ConfigureAwait(false);
        return GrpcContractMapper.ToGrpc(result);
    }
}

/// <summary>Implements index lifecycle and document operations over typed protobuf messages.</summary>
public sealed class LeanCorpusIndexGrpcService(IIndexService indices, IDocumentService documents) : GrpcContracts.IndexService.IndexServiceBase
{
    /// <inheritdoc />
    public override async Task<GrpcContracts.ListIndexesResponse> ListIndexes(Empty request, ServerCallContext context) =>
        GrpcContractMapper.ToGrpc(await indices.ListAsync(context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task<GrpcContracts.IndexMutationResponse> CreateIndex(GrpcContracts.CreateIndexRequest request, ServerCallContext context)
    {
        IndexSchema schema = GrpcContractMapper.FromStruct<IndexSchema>(request.Schema, "An index schema is required.");
        MutableIndexSettings settings = GrpcContractMapper.FromStruct<MutableIndexSettings>(request.Settings, "Index settings are required.");
        ContractCreateRequest contract = new(request.IndexName, schema, new IndexTopologySettings(request.ShardCount, request.ReplicaCount), settings);
        return GrpcContractMapper.ToGrpc(await indices.CreateAsync(contract, context.CancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public override async Task<GrpcContracts.DeleteIndexResponse> DeleteIndex(GrpcContracts.DeleteIndexRequest request, ServerCallContext context) =>
        GrpcContractMapper.ToGrpc(await indices.DeleteAsync(new ContractDeleteRequest(request.IndexName, request.ConfirmationToken), context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task<GrpcContracts.IndexMutationResponse> UpdateSettings(GrpcContracts.UpdateSettingsRequest request, ServerCallContext context)
    {
        MutableIndexSettings settings = GrpcContractMapper.FromStruct<MutableIndexSettings>(request.Settings, "Index settings are required.");
        return GrpcContractMapper.ToGrpc(await indices.UpdateSettingsAsync(new ContractUpdateRequest(request.IndexName, settings, request.ConfirmationToken), context.CancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public override async Task<GrpcContracts.IndexSchemaResponse> GetSchema(GrpcContracts.GetIndexRequest request, ServerCallContext context) =>
        GrpcContractMapper.ToGrpc(await indices.GetSchemaAsync(request.IndexName, context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task<GrpcContracts.IndexStatisticsResponse> GetStatistics(GrpcContracts.GetIndexRequest request, ServerCallContext context) =>
        GrpcContractMapper.ToGrpc(await indices.GetStatisticsAsync(request.IndexName, context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task<GrpcContracts.BulkDocumentsResponse> BulkDocuments(GrpcContracts.BulkDocumentsRequest request, ServerCallContext context)
    {
        BulkDocumentOperation[] operations = request.Operations.Select(operation => new BulkDocumentOperation(
            GrpcContractMapper.ParseEnum<DocumentOperationKind>(operation.Kind),
            operation.DocumentId,
            operation.Document is null ? null : GrpcContractMapper.ToJsonElement(operation.Document),
            string.IsNullOrWhiteSpace(operation.IdempotencyKey) ? null : operation.IdempotencyKey)).ToArray();
        ContractBulkRequest contract = new(request.IndexName, operations, request.Refresh, string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey);
        return GrpcContractMapper.ToGrpc(await documents.BulkAsync(contract, context.CancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public override async Task<GrpcContracts.RefreshIndexResponse> RefreshIndex(GrpcContracts.RefreshIndexRequest request, ServerCallContext context) =>
        GrpcContractMapper.ToGrpc(await indices.RefreshAsync(new ContractRefreshRequest(request.IndexName), context.CancellationToken).ConfigureAwait(false));
}

/// <summary>Implements bounded Community inspection over gRPC.</summary>
public sealed class LeanCorpusInspectionGrpcService(IInspectionService service) : GrpcContracts.InspectionService.InspectionServiceBase
{
    /// <inheritdoc />
    public override async Task<GrpcContracts.InspectionResponse> Inspect(GrpcContracts.InspectionRequest request, ServerCallContext context)
    {
        InspectionResource resource = GrpcContractMapper.ParseEnum<InspectionResource>(request.Resource);
        ContractInspectionRequest contract = new(resource, request.Limit == 0 ? 100 : request.Limit, request.Arguments);
        return GrpcContractMapper.ToGrpc(await service.InspectAsync(request.IndexName, contract, context.CancellationToken).ConfigureAwait(false));
    }
}

/// <summary>Implements Community health and readiness over gRPC.</summary>
public sealed class LeanCorpusHealthGrpcService(IHealthService service) : GrpcContracts.HealthService.HealthServiceBase
{
    /// <inheritdoc />
    public override async Task<GrpcContracts.HealthResponse> GetHealth(Empty request, ServerCallContext context) =>
        GrpcContractMapper.ToGrpc(await service.GetHealthAsync(context.CancellationToken).ConfigureAwait(false));

    /// <inheritdoc />
    public override async Task<GrpcContracts.ReadinessResponse> GetReadiness(Empty request, ServerCallContext context) =>
        GrpcContractMapper.ToGrpc(await service.GetReadinessAsync(context.CancellationToken).ConfigureAwait(false));
}
