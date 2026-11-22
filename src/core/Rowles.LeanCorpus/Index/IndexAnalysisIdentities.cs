using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Reads the analysis identities an index recorded for its analysed fields, and checks a query-time
/// analyser against them (ADR051).
/// </summary>
/// <remarks>
/// <see cref="IndexWriter"/> enforces the recorded identities when it writes. Query analysis is
/// outside the index, so call <see cref="EnsureCompatible"/> where queries are built, for example at
/// start-up, to detect a query analyser that no longer matches the indexed terms.
/// </remarks>
public static class IndexAnalysisIdentities
{
    /// <summary>Reads the identities recorded by the latest readable commit.</summary>
    /// <param name="directory">The index directory.</param>
    /// <returns>
    /// The analysis identity of every analysed field, by field name; empty when the index has no commit;
    /// or <see langword="null"/> when the latest commit was written before identities were recorded
    /// (LeanCorpus 3.x and earlier).
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="directory"/> is <see langword="null"/>.</exception>
    public static IReadOnlyDictionary<string, string>? Read(MMapDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        var recovery = IndexRecovery.RecoverLatestCommit(directory.DirectoryPath, cleanupOrphans: false);
        if (recovery is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);
        return recovery.FieldAnalysisIdentities is null
            ? null
            : new Dictionary<string, string>(recovery.FieldAnalysisIdentities, StringComparer.Ordinal);
    }

    /// <summary>
    /// Throws <see cref="AnalysisIdentityMismatchException"/> when <paramref name="analyser"/> does
    /// not produce the analysis the index recorded for <paramref name="fieldName"/>.
    /// </summary>
    /// <param name="directory">The index directory.</param>
    /// <param name="fieldName">The field the analyser builds queries for.</param>
    /// <param name="analyser">The query-time analyser.</param>
    /// <remarks>
    /// A field the index has not recorded passes, because it has no analysed terms, and so does a field
    /// recorded as <see cref="AnalysisIdentity.Unidentified"/>, which cannot be verified. An index written
    /// before identities were recorded fails for any field that has segments behind it, because its
    /// analysis is unknown.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null"/>.</exception>
    /// <exception cref="AnalysisIdentityMismatchException">Thrown when the identities differ.</exception>
    public static void EnsureCompatible(MMapDirectory directory, string fieldName, IAnalyser analyser)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(analyser);

        string current = AnalysisIdentityTracker.Effective(analyser, charFiltered: false);
        var recovery = IndexRecovery.RecoverLatestCommit(directory.DirectoryPath, cleanupOrphans: false);
        if (recovery is null)
            return;
        if (recovery.FieldAnalysisIdentities is null)
        {
            if (recovery.SegmentInfos.Count > 0)
                throw new AnalysisIdentityMismatchException(fieldName, recordedIdentity: null, current);
            return;
        }

        // An unidentified field cannot be verified: its analyser declared no identity, or the writer
        // applied character filters that the query analyser does not.
        if (recovery.FieldAnalysisIdentities.TryGetValue(fieldName, out var recorded)
            && !string.Equals(recorded, AnalysisIdentity.Unidentified, StringComparison.Ordinal)
            && !string.Equals(recorded, current, StringComparison.Ordinal))
        {
            throw new AnalysisIdentityMismatchException(fieldName, recorded, current);
        }
    }
}
