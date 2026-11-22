using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Index;

/// <summary>
/// Thrown when a field would be written with an analysis identity different from the one its index
/// recorded, so its new terms would not match its existing terms or the queries built for them
/// (ADR051).
/// </summary>
/// <remarks>
/// Reindex the field into a new index with the new analyser, or keep the recorded analyser. To
/// record the new identity and keep the existing terms anyway, set
/// <see cref="IndexWriterConfig.AcceptAnalysisIdentityChanges"/>.
/// </remarks>
public sealed class AnalysisIdentityMismatchException : InvalidOperationException
{
    /// <summary>Initialises a new exception for a field whose analysis identity differs from the recorded one.</summary>
    /// <param name="fieldName">The field.</param>
    /// <param name="recordedIdentity">The recorded identity, or <see langword="null"/> when the index predates recorded identities.</param>
    /// <param name="currentIdentity">The identity of the analyser now configured for the field.</param>
    public AnalysisIdentityMismatchException(string fieldName, string? recordedIdentity, string currentIdentity)
        : base(CreateMessage(fieldName, recordedIdentity, currentIdentity))
    {
        FieldName = fieldName;
        RecordedIdentity = recordedIdentity;
        CurrentIdentity = currentIdentity;
    }

    /// <summary>Gets the field whose analysis changed.</summary>
    public string FieldName { get; }

    /// <summary>
    /// Gets the identity the index recorded for the field, or <see langword="null"/> when the index
    /// was written before analysis identities were recorded (LeanCorpus 3.x and earlier).
    /// </summary>
    public string? RecordedIdentity { get; }

    /// <summary>Gets the identity of the analyser now configured for the field.</summary>
    public string CurrentIdentity { get; }

    private static string CreateMessage(string fieldName, string? recordedIdentity, string currentIdentity)
        => recordedIdentity is null
            ? $"Field '{fieldName}' would be analysed as '{currentIdentity}', but this index was written before analysis " +
              "identities were recorded (LeanCorpus 3.x or earlier), so its existing terms come from an unknown, older " +
              "analysis. Reindex from source, or set IndexWriterConfig.AcceptAnalysisIdentityChanges to record the current " +
              "identity and keep the existing terms."
            : $"Field '{fieldName}' was indexed as '{recordedIdentity}', but its analyser is now '{currentIdentity}'. " +
              "Its existing terms would not match new terms or queries. Reindex from source, restore the previous analyser, " +
              "or set IndexWriterConfig.AcceptAnalysisIdentityChanges to record the new identity and keep the existing terms.";
}
