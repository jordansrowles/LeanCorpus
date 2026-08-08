namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Evaluates feature entitlement without exposing licence implementation details.</summary>
public interface IEntitlementEvaluator
{
    /// <summary>Checks whether a feature is available.</summary>
    ValueTask<EntitlementDecision> EvaluateAsync(ServerFeature feature, CancellationToken cancellationToken = default);
}
