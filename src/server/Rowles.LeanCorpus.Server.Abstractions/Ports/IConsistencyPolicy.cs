using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Resolves the consistency available for an operation.</summary>
public interface IConsistencyPolicy
{
    /// <summary>Checks and resolves a requested consistency level.</summary>
    ValueTask<ConsistencyDecision> ResolveAsync(OperationContext context, RequestedConsistency requested, CancellationToken cancellationToken = default);
}
