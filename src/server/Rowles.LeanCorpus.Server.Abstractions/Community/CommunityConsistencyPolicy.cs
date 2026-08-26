using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Supports local consistency only.</summary>
public sealed class CommunityConsistencyPolicy : IConsistencyPolicy
{
    /// <inheritdoc />
    public ValueTask<ConsistencyDecision> ResolveAsync(OperationContext context, RequestedConsistency requested, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(requested is RequestedConsistency.Local or RequestedConsistency.Primary or RequestedConsistency.ReadYourWrites
            ? new ConsistencyDecision(true, RequestedConsistency.Local)
            : new ConsistencyDecision(false, RequestedConsistency.Local, "Community Server supports local consistency only."));
}
