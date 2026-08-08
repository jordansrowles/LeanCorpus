using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Observes local lifecycle transitions without adding behaviour.</summary>
public sealed class CommunityIndexLifecycleInterceptor : IIndexLifecycleInterceptor
{
    /// <inheritdoc />
    public ValueTask OnTransitionAsync(IndexLifecycleEvent transition, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
