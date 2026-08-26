using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Routes every Community operation to the local process.</summary>
public sealed class CommunityOperationRouter : IOperationRouter
{
    /// <inheritdoc />
    public ValueTask<OperationRoute> RouteAsync(OperationRouteRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<OperationRoute>(new LocalRoute());
}
