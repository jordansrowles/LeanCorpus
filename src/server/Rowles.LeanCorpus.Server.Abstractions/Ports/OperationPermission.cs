using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Identifies a requested operation permission.</summary>
public sealed record OperationPermission(OperationContext Context, EndpointAccess Access);
