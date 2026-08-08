namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Routes operations without exposing distributed implementation details.</summary>
public interface IOperationRouter
{
    /// <summary>Resolves the execution destination for an operation.</summary>
    ValueTask<OperationRoute> RouteAsync(OperationRouteRequest request, CancellationToken cancellationToken = default);
}
