namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Lists the stable REST endpoint matrix for version one.</summary>
public static class ServerEndpointCatalog
{
    /// <summary>Gets all version-one endpoints and their required interception ports.</summary>
    public static IReadOnlyList<EndpointDefinition> All { get; } =
    [
        new("GET", "/v1/health", ApiEdition.Community, EndpointAccess.Public, OperationKind.ReadHealth, [InterceptionPort.Authentication]),
        new("GET", "/v1/ready", ApiEdition.Community, EndpointAccess.Public, OperationKind.ReadReadiness, [InterceptionPort.Authentication]),
        new("GET", "/v1/indices", ApiEdition.Community, EndpointAccess.Public, OperationKind.ListIndexes, [InterceptionPort.Authentication, InterceptionPort.Authorisation]),
        new("PUT", "/v1/indices/{name}", ApiEdition.Community, EndpointAccess.Administrative, OperationKind.CreateIndex, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Lifecycle, InterceptionPort.Audit]),
        new("DELETE", "/v1/indices/{name}", ApiEdition.Community, EndpointAccess.Administrative, OperationKind.DeleteIndex, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Lifecycle, InterceptionPort.Audit]),
        new("PATCH", "/v1/indices/{name}/settings", ApiEdition.Community, EndpointAccess.Administrative, OperationKind.UpdateIndexSettings, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("POST", "/v1/indices/{name}/documents:bulk", ApiEdition.Community, EndpointAccess.Public, OperationKind.WriteDocuments, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Routing, InterceptionPort.WriteAcknowledgement, InterceptionPort.Audit]),
        new("POST", "/v1/indices/{name}/refresh", ApiEdition.Community, EndpointAccess.Public, OperationKind.RefreshIndex, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("POST", "/v1/indices/{name}/search", ApiEdition.Community, EndpointAccess.Public, OperationKind.Search, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Routing, InterceptionPort.Consistency, InterceptionPort.Audit]),
        new("POST", "/v1/indices/{name}/explain", ApiEdition.Community, EndpointAccess.Public, OperationKind.Search, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Routing, InterceptionPort.Audit]),
        new("GET", "/v1/indices/{name}/schema", ApiEdition.Community, EndpointAccess.Public, OperationKind.ReadIndexMetadata, [InterceptionPort.Authentication, InterceptionPort.Authorisation]),
        new("GET", "/v1/indices/{name}/stats", ApiEdition.Community, EndpointAccess.Public, OperationKind.ReadIndexMetadata, [InterceptionPort.Authentication, InterceptionPort.Authorisation]),
        new("GET", "/v1/indices/{name}/inspection/{resource}", ApiEdition.Community, EndpointAccess.Administrative, OperationKind.Inspect, [InterceptionPort.Authentication, InterceptionPort.Authorisation, InterceptionPort.Inspection, InterceptionPort.Audit]),
        new("GET", "/v1/cluster", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.ReadCluster, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("GET", "/v1/indices/{name}/shards", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.ReadShards, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("POST", "/v1/admin/nodes/{id}:drain", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.DrainNode, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Routing, InterceptionPort.Audit]),
        new("POST", "/v1/admin/shards/{id}:recover", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.RecoverShard, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Routing, InterceptionPort.Audit]),
        new("GET", "/v1/admin/license", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.ManageLicence, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("POST", "/v1/admin/license:validate", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.ManageLicence, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("POST", "/v1/admin/snapshots", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.ManageSnapshot, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("POST", "/v1/admin/snapshots/{id}:restore", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.ManageSnapshot, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit]),
        new("GET", "/v1/admin/diagnostics", ApiEdition.Enterprise, EndpointAccess.Administrative, OperationKind.ReadDiagnostics, [InterceptionPort.Authorisation, InterceptionPort.Entitlement, InterceptionPort.Audit])
    ];
}
