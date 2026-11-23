using System.Collections.Concurrent;

namespace Rowles.LeanCorpus.Codecs.Hnsw;

/// <summary>
/// Outcome of the full validation of each vector graph file, kept by a segment reader for its
/// lifetime (ADR052). Segment files are immutable while a reader is open, so a graph that passed
/// the checksum, level-zero and reachability checks once needs only its cheap header checks when
/// an evicted reader state reopens it, and one that failed permanently is not re-validated on
/// every query. Transient open failures are never recorded.
/// </summary>
internal sealed class VectorGraphValidationMemo
{
    private readonly ConcurrentDictionary<string, string?> _outcomes = new(StringComparer.Ordinal);

    /// <summary>Returns whether the field's graph passed full validation on this reader.</summary>
    internal bool IsValidated(string fieldName)
        => _outcomes.TryGetValue(fieldName, out string? failure) && failure is null;

    /// <summary>Returns the remembered permanent failure for the field's graph, if any.</summary>
    internal bool TryGetFailure(string fieldName, out string? failure)
        => _outcomes.TryGetValue(fieldName, out failure) && failure is not null;

    internal void RecordValidated(string fieldName) => _outcomes[fieldName] = null;

    internal void RecordFailure(string fieldName, string failure) => _outcomes[fieldName] = failure;
}
