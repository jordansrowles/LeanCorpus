using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Executes operations against a selected local physical index.</summary>
/// <remarks>
/// Implementations are a trusted composition boundary. The supplied operation context must
/// already have crossed the host authentication and authorisation boundary; this interface
/// deliberately has no transport, router or authentication dependency.
/// </remarks>
public interface ILocalIndexExecutor
{
    /// <summary>Applies a document batch after orchestration has established the context and target.</summary>
    ValueTask<LocalWriteResult> WriteAsync(
        OperationContext context,
        LocalIndexHandle index,
        BulkDocumentsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Searches a selected local physical index after orchestration has established the context and target.</summary>
    ValueTask<SearchResponse> SearchAsync(
        OperationContext context,
        LocalIndexHandle index,
        SearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Explains a query against a selected local physical index.</summary>
    ValueTask<ExplainResponse> ExplainAsync(
        OperationContext context,
        LocalIndexHandle index,
        ExplainRequest request,
        CancellationToken cancellationToken = default);
}
