using Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Services;

/// <summary>Provides Enterprise administration operations.</summary>
public interface IAdministrationService
{
    /// <summary>Reads cluster state.</summary>
    ValueTask<ServiceResult<ClusterInfoResponse>> GetClusterAsync(CancellationToken cancellationToken = default);
    /// <summary>Reads index shard placement.</summary>
    ValueTask<ServiceResult<ShardPlacementResponse>> GetShardsAsync(string indexName, CancellationToken cancellationToken = default);
    /// <summary>Drains a node.</summary>
    ValueTask<ServiceResult<DrainNodeResponse>> DrainNodeAsync(DrainNodeRequest request, CancellationToken cancellationToken = default);
    /// <summary>Recovers a shard.</summary>
    ValueTask<ServiceResult<RecoverShardResponse>> RecoverShardAsync(RecoverShardRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads licence status.</summary>
    ValueTask<ServiceResult<LicenceStatusResponse>> GetLicenceAsync(CancellationToken cancellationToken = default);
    /// <summary>Validates a licence envelope.</summary>
    ValueTask<ServiceResult<ValidateLicenceResponse>> ValidateLicenceAsync(ValidateLicenceRequest request, CancellationToken cancellationToken = default);
    /// <summary>Creates a snapshot.</summary>
    ValueTask<ServiceResult<SnapshotResponse>> CreateSnapshotAsync(SnapshotRequest request, CancellationToken cancellationToken = default);
    /// <summary>Restores a snapshot.</summary>
    ValueTask<ServiceResult<RestoreSnapshotResponse>> RestoreSnapshotAsync(RestoreSnapshotRequest request, CancellationToken cancellationToken = default);
    /// <summary>Reads a redacted diagnostic report.</summary>
    ValueTask<ServiceResult<DiagnosticsResponse>> GetDiagnosticsAsync(DiagnosticsRequest request, CancellationToken cancellationToken = default);
}
