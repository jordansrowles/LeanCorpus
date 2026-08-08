namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Contains non-authoritative timings for a completed search.</summary>
public sealed record SearchTiming(long TookMilliseconds, long? RewriteMilliseconds = null, long? QueryMilliseconds = null, long? FetchMilliseconds = null);
