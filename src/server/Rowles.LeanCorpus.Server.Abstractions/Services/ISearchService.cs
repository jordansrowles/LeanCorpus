using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

namespace Rowles.LeanCorpus.Server.Abstractions.Services;

/// <summary>Executes searches and explanations.</summary>
public interface ISearchService
{
    /// <summary>Executes a search against an index.</summary>
    ValueTask<ServiceResult<SearchResponse>> SearchAsync(string indexName, SearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Explains a score for one document.</summary>
    ValueTask<ServiceResult<ExplainResponse>> ExplainAsync(string indexName, ExplainRequest request, CancellationToken cancellationToken = default);
}
