using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Describes a write awaiting acknowledgement.</summary>
public sealed record WriteCommitState(OperationContext Context, string IndexName, long SequenceNumber);
