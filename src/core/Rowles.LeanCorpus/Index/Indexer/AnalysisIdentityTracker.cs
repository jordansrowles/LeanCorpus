using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Records and enforces the analysis identity of every analysed field for one writer (ADR051).
/// </summary>
/// <remarks>
/// The configured analysers are fixed for a writer's lifetime, so each field's current identity is
/// resolved once. The recorded map comes from the latest commit and is written into every commit.
/// Every recorded field is checked when the writer opens, and a new field when a document containing
/// it is validated, before the writer mutates anything. A rejection therefore leaves the writer usable.
/// </remarks>
internal sealed class AnalysisIdentityTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _recorded;
    private readonly HashSet<string> _verified = new(StringComparer.Ordinal);

    // Read without the lock on the per-document path; written only under the lock.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _verifiedFast = new(StringComparer.Ordinal);
    private readonly Func<string, string> _currentIdentity;
    private readonly bool _acceptChanges;
    private bool _legacyContent;

    private AnalysisIdentityTracker(
        Dictionary<string, string> recorded, bool legacyContent, Func<string, string> currentIdentity, bool acceptChanges)
    {
        _recorded = recorded;
        _legacyContent = legacyContent;
        _currentIdentity = currentIdentity;
        _acceptChanges = acceptChanges;
    }

    /// <summary>Creates the tracker for a writer's configuration and latest commit.</summary>
    /// <param name="config">The writer configuration.</param>
    /// <param name="defaultAnalyser">The writer's effective default analyser.</param>
    /// <param name="recorded">The latest commit's identities, or <see langword="null"/> when it predates them.</param>
    /// <param name="hasCommittedSegments">Whether the latest commit has any segment.</param>
    public static AnalysisIdentityTracker Create(
        IndexWriterConfig config,
        IAnalyser defaultAnalyser,
        IReadOnlyDictionary<string, string>? recorded,
        bool hasCommittedSegments)
    {
        var map = recorded is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(recorded, StringComparer.Ordinal);

        // Writer-level character filters change every field's output, so no field can claim the
        // analyser's identity while they are configured.
        bool charFiltered = config.CharFilters.Count > 0;
        var fieldAnalysers = config.FieldAnalysers;
        string defaultIdentity = Effective(defaultAnalyser, charFiltered);
        var fieldIdentities = new Dictionary<string, string>(fieldAnalysers.Count, StringComparer.Ordinal);
        foreach (var (field, analyser) in fieldAnalysers)
            fieldIdentities[field] = Effective(analyser, charFiltered);

        var tracker = new AnalysisIdentityTracker(
            map,
            legacyContent: recorded is null && hasCommittedSegments,
            field => fieldIdentities.GetValueOrDefault(field, defaultIdentity),
            config.AcceptAnalysisIdentityChanges);
        tracker.VerifyRecordedFields();
        return tracker;
    }

    /// <summary>The identity recorded for a field analysed by <paramref name="analyser"/>.</summary>
    internal static string Effective(IAnalyser analyser, bool charFiltered)
        => charFiltered ? AnalysisIdentity.Unidentified : AnalysisIdentity.Of(analyser) ?? AnalysisIdentity.Unidentified;

    /// <summary>
    /// Checks a field before it is analysed, and records its identity. Throws
    /// <see cref="AnalysisIdentityMismatchException"/> when the field's analysis would change.
    /// </summary>
    public void EnsureField(string fieldName)
    {
        if (_verifiedFast.ContainsKey(fieldName))
            return;

        lock (_gate)
        {
            if (_verified.Contains(fieldName))
                return;

            string current = _currentIdentity(fieldName);
            if (_recorded.TryGetValue(fieldName, out var recorded))
            {
                if (!string.Equals(recorded, current, StringComparison.Ordinal))
                {
                    if (!_acceptChanges)
                        throw new AnalysisIdentityMismatchException(fieldName, recorded, current);
                    _recorded[fieldName] = current;
                }
            }
            else
            {
                // Pre-identity content may hold this field under an older, unknown analysis.
                if (_legacyContent && !_acceptChanges)
                    throw new AnalysisIdentityMismatchException(fieldName, recordedIdentity: null, current);
                _recorded[fieldName] = current;
            }

            _verified.Add(fieldName);
            _verifiedFast.TryAdd(fieldName, 0);
        }
    }

    /// <summary>Gets the identities to write into the next commit.</summary>
    public Dictionary<string, string> Snapshot()
    {
        lock (_gate)
            return new Dictionary<string, string>(_recorded, StringComparer.Ordinal);
    }

    /// <summary>
    /// After a commit that removed every segment, no recorded term remains, so earlier identities
    /// and pre-identity content no longer constrain new fields.
    /// </summary>
    public void OnCommitted(bool hasSegments)
    {
        if (hasSegments)
            return;
        lock (_gate)
        {
            _legacyContent = false;
            foreach (var field in _recorded.Keys.Where(field => !_verified.Contains(field)).ToArray())
                _recorded.Remove(field);
        }
    }

    private void VerifyRecordedFields()
    {
        foreach (var field in _recorded.Keys.ToArray())
            EnsureField(field);
    }
}
