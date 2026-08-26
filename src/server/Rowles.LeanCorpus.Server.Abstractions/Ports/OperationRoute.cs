namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Executes the operation in the local process.</summary>
public sealed record LocalRoute;

/// <summary>Forwards the operation to a trusted remote target.</summary>
public sealed record RemoteRoute(string TargetId);

/// <summary>Rejects the operation before execution.</summary>
public sealed record RejectedRoute(string Reason);

/// <summary>Describes the closed set of execution destinations.</summary>
public union OperationRoute(LocalRoute, RemoteRoute, RejectedRoute)
{
    /// <summary>Gets the route category without reintroducing nullable route state.</summary>
    public RouteTargetKind TargetKind => this switch
    {
        LocalRoute => RouteTargetKind.Local,
        RemoteRoute => RouteTargetKind.Remote,
        RejectedRoute => RouteTargetKind.Rejected
    };
}
