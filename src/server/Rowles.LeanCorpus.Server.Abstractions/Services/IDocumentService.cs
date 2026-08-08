using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

namespace Rowles.LeanCorpus.Server.Abstractions.Services;

/// <summary>Applies document write batches.</summary>
public interface IDocumentService
{
    /// <summary>Applies a batch of document operations.</summary>
    ValueTask<ServiceResult<BulkDocumentsResponse>> BulkAsync(BulkDocumentsRequest request, CancellationToken cancellationToken = default);
}
