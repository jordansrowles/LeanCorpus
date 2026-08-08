using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Describes an index lifecycle transition.</summary>
public sealed record IndexLifecycleEvent(OperationContext Context, string IndexName, IndexLifecycleStage Stage);
