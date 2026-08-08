using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

namespace Rowles.LeanCorpus.Server.Abstractions.Services;

/// <summary>Provides liveness and readiness information.</summary>
public interface IHealthService
{
    /// <summary>Reads process health.</summary>
    ValueTask<ServiceResult<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads service readiness.</summary>
    ValueTask<ServiceResult<ReadinessResponse>> GetReadinessAsync(CancellationToken cancellationToken = default);
}
