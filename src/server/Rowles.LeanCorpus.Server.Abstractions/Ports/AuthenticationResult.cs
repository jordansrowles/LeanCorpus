using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Contains the outcome of authenticating a request.</summary>
public sealed record AuthenticationResult(CallerIdentity Caller, bool IsAuthenticated, string? FailureReason = null);
