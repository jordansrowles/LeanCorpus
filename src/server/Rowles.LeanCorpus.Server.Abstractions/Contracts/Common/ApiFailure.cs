namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Represents a stable, safe failure returned by the server API.</summary>
/// <param name="Code">Machine-readable failure code.</param>
/// <param name="Message">Safe message suitable for callers.</param>
/// <param name="Retryable">Whether retrying the same operation may succeed.</param>
/// <param name="Details">Optional safe, structured details.</param>
public sealed record ApiFailure(
    string Code,
    string Message,
    bool Retryable = false,
    IReadOnlyDictionary<string, string>? Details = null);
