using System.Text.Json.Serialization;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

namespace Rowles.LeanCorpus.Server.Abstractions.Serialisation;

/// <summary>Provides Native AOT-safe JSON metadata for stable server contracts.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CreateIndexRequest))]
[JsonSerializable(typeof(UpdateIndexSettingsRequest))]
[JsonSerializable(typeof(DeleteIndexRequest))]
[JsonSerializable(typeof(BulkDocumentsRequest))]
[JsonSerializable(typeof(BulkDocumentsResponse))]
[JsonSerializable(typeof(RefreshIndexRequest))]
[JsonSerializable(typeof(RefreshIndexResponse))]
[JsonSerializable(typeof(SearchRequest))]
[JsonSerializable(typeof(SearchResponse))]
[JsonSerializable(typeof(ExplainRequest))]
[JsonSerializable(typeof(ExplainResponse))]
[JsonSerializable(typeof(InspectionRequest))]
[JsonSerializable(typeof(InspectionResponse))]
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(IndexHealthSummary))]
[JsonSerializable(typeof(ReadinessResponse))]
[JsonSerializable(typeof(ClusterInfoResponse))]
[JsonSerializable(typeof(ShardPlacementResponse))]
[JsonSerializable(typeof(DrainNodeRequest))]
[JsonSerializable(typeof(DrainNodeResponse))]
[JsonSerializable(typeof(RecoverShardRequest))]
[JsonSerializable(typeof(RecoverShardResponse))]
[JsonSerializable(typeof(LicenceStatusResponse))]
[JsonSerializable(typeof(ValidateLicenceRequest))]
[JsonSerializable(typeof(ValidateLicenceResponse))]
[JsonSerializable(typeof(SnapshotRequest))]
[JsonSerializable(typeof(SnapshotResponse))]
[JsonSerializable(typeof(RestoreSnapshotRequest))]
[JsonSerializable(typeof(RestoreSnapshotResponse))]
[JsonSerializable(typeof(DiagnosticsRequest))]
[JsonSerializable(typeof(DiagnosticsResponse))]
[JsonSerializable(typeof(ApiFailure))]
public partial class ServerJsonSerialiserContext : JsonSerializerContext;
