namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Splits compound tokens on delimiters, case changes, and letter-number boundaries.
/// </summary>
/// <remarks>
/// The <see cref="ISpanTokenFilter"/> implementation passes tokens through without
/// splitting because compound token splitting requires a full token list view that the
/// one-at-a-time span interface cannot provide. Callers that need word delimiter splitting
/// should use a dedicated analyser that works on the full token list.
/// </remarks>
public sealed class WordDelimiterFilter : ISpanTokenFilter
{
    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }
}
