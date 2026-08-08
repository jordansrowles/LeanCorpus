namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Describes a versioned REST endpoint and its ownership.</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Route">Versioned route template.</param>
/// <param name="Edition">Edition required to register the route.</param>
/// <param name="Access">Public or administrative access classification.</param>
/// <param name="Operation">Operation represented by the route.</param>
/// <param name="RequiredPorts">Ports the endpoint must use.</param>
public sealed record EndpointDefinition(
    string Method,
    string Route,
    ApiEdition Edition,
    EndpointAccess Access,
    OperationKind Operation,
    IReadOnlyList<InterceptionPort> RequiredPorts);
