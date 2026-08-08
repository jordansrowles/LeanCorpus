using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Abstractions.Services;

/// <summary>Manages index schemas, settings, and lifecycle.</summary>
public interface IIndexService
{
    /// <summary>Lists visible indices.</summary>
    ValueTask<ServiceResult<IReadOnlyList<IndexSummary>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an index.</summary>
    ValueTask<ServiceResult<IndexSummary>> CreateAsync(CreateIndexRequest request, CancellationToken cancellationToken = default);

    /// <summary>Deletes an index.</summary>
    ValueTask<ServiceResult<bool>> DeleteAsync(DeleteIndexRequest request, CancellationToken cancellationToken = default);

    /// <summary>Updates mutable index settings.</summary>
    ValueTask<ServiceResult<IndexSummary>> UpdateSettingsAsync(UpdateIndexSettingsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads an index schema.</summary>
    ValueTask<ServiceResult<IndexSchemaResponse>> GetSchemaAsync(string indexName, CancellationToken cancellationToken = default);

    /// <summary>Reads index statistics.</summary>
    ValueTask<ServiceResult<IndexStatisticsResponse>> GetStatisticsAsync(string indexName, CancellationToken cancellationToken = default);

    /// <summary>Refreshes an index.</summary>
    ValueTask<ServiceResult<RefreshIndexResponse>> RefreshAsync(RefreshIndexRequest request, CancellationToken cancellationToken = default);
}
