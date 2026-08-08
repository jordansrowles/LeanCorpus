using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Provides the information required to route an operation.</summary>
public sealed record OperationRouteRequest(OperationContext Context, bool RequiresWriteOwnership);
