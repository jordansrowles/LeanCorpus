namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Observes index lifecycle transitions.</summary>
public interface IIndexLifecycleInterceptor
{
    /// <summary>Observes an index lifecycle transition.</summary>
    ValueTask OnTransitionAsync(IndexLifecycleEvent transition, CancellationToken cancellationToken = default);
}
