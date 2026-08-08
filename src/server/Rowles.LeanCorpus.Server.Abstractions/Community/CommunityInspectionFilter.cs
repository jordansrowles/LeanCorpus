using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Allows bounded local inspection requests.</summary>
public sealed class CommunityInspectionFilter : IInspectionFilter
{
    /// <inheritdoc />
    public ValueTask<InspectionDecision> EvaluateAsync(OperationContext context, InspectionRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(request.Limit is > 0 and <= 1_000
            ? new InspectionDecision(true, 1_000)
            : new InspectionDecision(false, 1_000, "Inspection limits must be between 1 and 1000."));
}
