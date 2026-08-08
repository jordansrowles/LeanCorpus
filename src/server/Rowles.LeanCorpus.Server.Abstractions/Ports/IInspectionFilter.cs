using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Restricts inspection requests before internal readers are accessed.</summary>
public interface IInspectionFilter
{
    /// <summary>Checks whether an inspection request is allowed and bounded.</summary>
    ValueTask<InspectionDecision> EvaluateAsync(OperationContext context, InspectionRequest request, CancellationToken cancellationToken = default);
}
