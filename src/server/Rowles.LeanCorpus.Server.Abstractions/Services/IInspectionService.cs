using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

namespace Rowles.LeanCorpus.Server.Abstractions.Services;

/// <summary>Provides bounded, read-only inspection operations.</summary>
public interface IInspectionService
{
    /// <summary>Inspects an index resource.</summary>
    ValueTask<ServiceResult<InspectionResponse>> InspectAsync(string indexName, InspectionRequest request, CancellationToken cancellationToken = default);
}
