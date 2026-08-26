using System.Globalization;
using System.Text.Json;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using GrpcContracts = Rowles.LeanCorpus.Server.Grpc.Contracts;
using ContractExplainResponse = Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.ExplainResponse;
using ContractHealthResponse = Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection.HealthResponse;
using ContractInspectionResponse = Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection.InspectionResponse;
using ContractReadinessResponse = Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection.ReadinessResponse;
using ContractSearchResponse = Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.SearchResponse;

namespace Rowles.LeanCorpus.Server.Grpc;

internal static class GrpcContractMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal static T FromStruct<T>(Struct? value, string missingMessage)
    {
        if (value is null)
            throw InvalidArgument(missingMessage);
        try
        {
            return JsonSerializer.Deserialize<T>(JsonFormatter.Default.Format(value), JsonOptions)
                ?? throw new JsonException(missingMessage);
        }
        catch (JsonException exception)
        {
            throw InvalidArgument(exception.Message);
        }
    }

    internal static JsonElement ToJsonElement(Struct value)
    {
        using JsonDocument document = JsonDocument.Parse(JsonFormatter.Default.Format(value));
        return document.RootElement.Clone();
    }

    internal static IReadOnlyList<object?>? FromList(ListValue? value)
    {
        if (value is null || value.Values.Count == 0)
            return null;
        return JsonSerializer.Deserialize<object?[]>(JsonFormatter.Default.Format(value), JsonOptions);
    }

    internal static TEnum ParseEnum<TEnum>(string value, TEnum? defaultValue = null) where TEnum : struct, System.Enum
    {
        if (string.IsNullOrWhiteSpace(value) && defaultValue.HasValue)
            return defaultValue.Value;
        if (System.Enum.TryParse(value, ignoreCase: true, out TEnum parsed) && System.Enum.IsDefined(parsed))
            return parsed;
        throw InvalidArgument($"'{value}' is not a valid {typeof(TEnum).Name} value.");
    }

    internal static Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.SortDefinition ToContract(GrpcContracts.SortDefinition value) =>
        new(value.Field, ParseEnum<SortDirection>(value.Direction, SortDirection.Descending));

    internal static Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.FacetDefinition ToContract(GrpcContracts.FacetDefinition value) =>
        new(
            value.Name,
            value.Field,
            ParseEnum<FacetKind>(value.Kind),
            value.HasSize ? value.Size : null,
            value.Ranges.Select(range => new Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.FacetRange(
                range.Key,
                range.HasFrom ? range.From : null,
                range.HasTo ? range.To : null)).ToArray());

    internal static GrpcContracts.ListIndexesResponse ToGrpc(ServiceResult<IReadOnlyList<IndexSummary>> result)
    {
        GrpcContracts.ListIndexesResponse response = new() { Metadata = ToGrpc(result.Metadata), Failure = ToGrpc(result.Failure) };
        if (result.Value is not null)
            response.Indices.AddRange(result.Value.Select(ToGrpc));
        return response;
    }

    internal static GrpcContracts.IndexMutationResponse ToGrpc(ServiceResult<IndexSummary> result) => new()
    {
        Metadata = ToGrpc(result.Metadata),
        Index = result.Value is null ? null : ToGrpc(result.Value),
        Failure = ToGrpc(result.Failure)
    };

    internal static GrpcContracts.DeleteIndexResponse ToGrpc(ServiceResult<bool> result) => new()
    {
        Metadata = ToGrpc(result.Metadata),
        Deleted = result.Value,
        Failure = ToGrpc(result.Failure)
    };

    internal static GrpcContracts.RefreshIndexResponse ToGrpc(ServiceResult<Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.RefreshIndexResponse> result) => new()
    {
        Metadata = ToGrpc(result.Metadata),
        IndexName = result.Value?.IndexName ?? string.Empty,
        CommitGeneration = result.Value?.CommitGeneration ?? 0,
        Failure = ToGrpc(result.Failure)
    };

    internal static GrpcContracts.BulkDocumentsResponse ToGrpc(ServiceResult<Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents.BulkDocumentsResponse> result)
    {
        GrpcContracts.BulkDocumentsResponse response = new()
        {
            Metadata = ToGrpc(result.Metadata),
            Acknowledged = result.Value?.Acknowledged ?? false,
            Failure = ToGrpc(result.Failure)
        };
        if (result.Value?.CommitGeneration is long generation)
            response.CommitGeneration = generation;
        if (result.Value?.WriteToken is { } token)
            response.WriteToken = ToGrpc(token);
        if (result.Value is not null)
        {
            response.Items.AddRange(result.Value.Items.Select(item => new GrpcContracts.BulkDocumentResult
            {
                DocumentId = item.DocumentId,
                Accepted = item.Accepted,
                Failure = ToGrpc(item.Failure)
            }));
        }
        return response;
    }

    internal static GrpcContracts.SearchResponse ToGrpc(ServiceResult<ContractSearchResponse> result)
    {
        GrpcContracts.SearchResponse response = new()
        {
            Metadata = ToGrpc(result.Metadata),
            Failure = ToGrpc(result.Failure)
        };
        if (result.Value is not { } value)
            return response;

        response.TotalHits = value.TotalHits;
        response.TotalHitsRelation = value.TotalHitsRelation.ToString();
        response.ScoringModel = value.ScoringModel.ToString();
        response.ShardsTotal = value.Shards.Total;
        response.ShardsSuccessful = value.Shards.Successful;
        response.ShardsFailed = value.Shards.Failed;
        response.ShardsSkipped = value.Shards.Skipped;
        response.TookMilliseconds = value.Timing.TookMilliseconds;
        response.IsPartial = value.IsPartial;
        response.Hits.AddRange(value.Hits.Select(hit => new GrpcContracts.SearchHit
        {
            DocumentId = hit.DocumentId,
            Score = hit.Score,
            Document = hit.Document is { } document ? ToStruct(document) : null,
            SortValues = ToList(hit.SortValues)
        }));
        if (value.NextSearchAfter is not null)
            response.NextSearchAfter = ToList(value.NextSearchAfter);
        if (value.Facets is not null)
        {
            response.Facets.AddRange(value.Facets.Select(facet =>
            {
                GrpcContracts.FacetResult mapped = new() { Name = facet.Name, Completeness = facet.Completeness.ToString() };
                mapped.Buckets.AddRange(facet.Buckets.Select(bucket => new GrpcContracts.FacetBucket { Key = bucket.Key, Count = bucket.Count }));
                return mapped;
            }));
        }
        return response;
    }

    internal static GrpcContracts.ExplainResponse ToGrpc(ServiceResult<ContractExplainResponse> result) => new()
    {
        Metadata = ToGrpc(result.Metadata),
        Explanation = result.Value is null ? null : ToGrpc(result.Value),
        Failure = ToGrpc(result.Failure)
    };

    internal static GrpcContracts.IndexSchemaResponse ToGrpc(ServiceResult<Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexSchemaResponse> result) => new()
    {
        Metadata = ToGrpc(result.Metadata),
        IndexName = result.Value?.IndexName ?? string.Empty,
        Schema = result.Value is null ? null : ToStruct(result.Value.Schema),
        SchemaHash = result.Value?.SchemaHash ?? string.Empty,
        Settings = result.Value is null ? null : ToStruct(result.Value.Settings),
        Failure = ToGrpc(result.Failure)
    };

    internal static GrpcContracts.IndexStatisticsResponse ToGrpc(ServiceResult<Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing.IndexStatisticsResponse> result) => new()
    {
        Metadata = ToGrpc(result.Metadata),
        IndexName = result.Value?.IndexName ?? string.Empty,
        SchemaHash = result.Value?.SchemaHash ?? string.Empty,
        DocumentCount = result.Value?.DocumentCount ?? 0,
        DeletedDocumentCount = result.Value?.DeletedDocumentCount ?? 0,
        StorageBytes = result.Value?.StorageBytes ?? 0,
        SegmentCount = result.Value?.SegmentCount ?? 0,
        CommitGeneration = result.Value?.CommitGeneration ?? 0,
        Failure = ToGrpc(result.Failure)
    };

    internal static GrpcContracts.InspectionResponse ToGrpc(ServiceResult<ContractInspectionResponse> result)
    {
        GrpcContracts.InspectionResponse response = new()
        {
            Metadata = ToGrpc(result.Metadata),
            Resource = result.Value?.Resource.ToString() ?? string.Empty,
            Data = result.Value is null ? null : Value.Parser.ParseJson(result.Value.Data.GetRawText()),
            IsTruncated = result.Value?.IsTruncated ?? false,
            Failure = ToGrpc(result.Failure)
        };
        if (result.Value?.MetadataEpoch is long epoch)
            response.MetadataEpoch = epoch;
        return response;
    }

    internal static GrpcContracts.HealthResponse ToGrpc(ServiceResult<ContractHealthResponse> result)
    {
        GrpcContracts.HealthResponse response = new()
        {
            Metadata = ToGrpc(result.Metadata),
            IsHealthy = result.Value?.IsHealthy ?? false,
            Status = result.Value?.Status ?? string.Empty,
            ObservedUtc = result.Value?.ObservedUtc.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
            Reason = result.Value?.Reason ?? string.Empty,
            Failure = ToGrpc(result.Failure)
        };
        if (result.Value?.Indices is not null)
        {
            response.Indices.AddRange(result.Value.Indices.Select(index => new GrpcContracts.IndexHealthSummary
            {
                IndexName = index.IndexName,
                IndexId = index.IndexId,
                Mode = index.Mode,
                VisibleGeneration = index.VisibleGeneration,
                DurableGeneration = index.DurableGeneration,
                PendingOperations = index.PendingOperations,
                LastSuccessfulCommitUtc = index.LastSuccessfulCommitUtc?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                LastCommitError = index.LastCommitError ?? string.Empty,
                ConsecutiveCommitFailures = index.ConsecutiveCommitFailures,
                ActiveSnapshotLeases = index.ActiveSnapshotLeases,
                IsInstalling = index.IsInstalling,
                IsUsable = index.IsUsable,
                IsDegraded = index.IsDegraded,
                LastInstallError = index.LastInstallError ?? string.Empty
            }));
        }
        return response;
    }

    internal static GrpcContracts.ReadinessResponse ToGrpc(ServiceResult<ContractReadinessResponse> result) => new()
    {
        Metadata = ToGrpc(result.Metadata),
        IsReady = result.Value?.IsReady ?? false,
        Status = result.Value?.Status ?? string.Empty,
        ObservedUtc = result.Value?.ObservedUtc.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        Reason = result.Value?.Reason ?? string.Empty,
        Failure = ToGrpc(result.Failure)
    };

    private static GrpcContracts.ResponseMetadata ToGrpc(ResponseMetadata metadata) => new()
    {
        RequestId = metadata.RequestId,
        ApiVersion = metadata.ApiVersion,
        ObservedUtc = metadata.GeneratedUtc.ToString("O", CultureInfo.InvariantCulture)
    };

    internal static WriteToken? ToContract(GrpcContracts.WriteToken? token) => token is null || string.IsNullOrWhiteSpace(token.IndexId)
        ? null
        : new WriteToken(token.Version, token.IndexId, token.SequenceNumber,
            token.HasCommitGeneration ? token.CommitGeneration : null,
            token.HasContentToken ? token.ContentToken : null);

    private static GrpcContracts.WriteToken ToGrpc(WriteToken token)
    {
        GrpcContracts.WriteToken value = new() { Version = token.Version, IndexId = token.IndexId, SequenceNumber = token.SequenceNumber };
        if (token.CommitGeneration is long generation) value.CommitGeneration = generation;
        if (token.ContentToken is long contentToken) value.ContentToken = contentToken;
        return value;
    }

    private static GrpcContracts.ContractFailure? ToGrpc(ApiFailure? failure) => failure is null ? null : new GrpcContracts.ContractFailure
    {
        Code = failure.Code,
        Message = failure.Message,
        Retryable = failure.Retryable,
        Details = failure.Details is null ? null : ToStruct(failure.Details)
    };

    private static GrpcContracts.IndexSummary ToGrpc(IndexSummary value) => new()
    {
        IndexName = value.IndexName,
        IndexId = value.IndexId,
        SchemaHash = value.SchemaHash,
        DocumentCount = value.DocumentCount,
        CreatedUtc = value.CreatedUtc.ToString("O", CultureInfo.InvariantCulture)
    };

    private static GrpcContracts.ExplainNode ToGrpc(ContractExplainResponse value)
    {
        GrpcContracts.ExplainNode result = new() { IsMatch = value.IsMatch, Description = value.Description };
        if (value.Score is float score)
            result.Score = score;
        if (value.Details is not null)
            result.Details.AddRange(value.Details.Select(ToGrpc));
        return result;
    }

    private static Struct ToStruct<T>(T value) => Struct.Parser.ParseJson(JsonSerializer.Serialize(value, JsonOptions));

    private static Struct ToStruct(JsonElement value) => Struct.Parser.ParseJson(value.GetRawText());

    private static ListValue? ToList(IReadOnlyList<object?>? value) => value is null
        ? null
        : ListValue.Parser.ParseJson(JsonSerializer.Serialize(value, JsonOptions));

    private static RpcException InvalidArgument(string message) => new(new Status(StatusCode.InvalidArgument, message));
}
