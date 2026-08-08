using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Allows features without a licence implementation.</summary>
public sealed class CommunityEntitlementEvaluator : IEntitlementEvaluator
{
    /// <inheritdoc />
    public ValueTask<EntitlementDecision> EvaluateAsync(ServerFeature feature, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new EntitlementDecision(true));
}
