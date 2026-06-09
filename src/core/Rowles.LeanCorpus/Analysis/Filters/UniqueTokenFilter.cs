namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Removes duplicate tokens while preserving the first occurrence.
/// </summary>
/// <remarks>
/// The <see cref="ISpanTokenFilter"/> implementation passes tokens through without
/// deduplication because uniqueness requires a full token list view that the
/// one-at-a-time span interface cannot provide. Callers that need deduplication
/// should use a dedicated analyser that works on the full token list.
/// </remarks>
public sealed class UniqueTokenFilter : ISpanTokenFilter
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
