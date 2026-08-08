namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Contains metadata included with every versioned server response.</summary>
/// <param name="RequestId">Identifier of the request that produced the response.</param>
/// <param name="ApiVersion">Server API version used for the response.</param>
/// <param name="GeneratedUtc">UTC time at which the response was generated.</param>
/// <param name="MetadataEpoch">Optional Enterprise metadata epoch.</param>
public sealed record ResponseMetadata(
    string RequestId,
    string ApiVersion,
    DateTimeOffset GeneratedUtc,
    long? MetadataEpoch = null);
