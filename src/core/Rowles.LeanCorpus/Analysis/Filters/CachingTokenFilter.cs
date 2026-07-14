namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Captures tokens as they pass through the filter chain, enabling multiple passes
/// over the same token stream without re-running the analysis pipeline.
/// </summary>
/// <remarks>
/// <para>Place this filter in the analysis chain, run
/// <c>Analyser.Analyse(...)</c>, then read <see cref="Tokens"/> to inspect the
/// captured stream. Call <see cref="Reset"/> before the next <c>Analyse</c> call
/// to clear the capture.</para>
/// <para>Each captured token's text is materialised as a <see cref="string"/> because
/// the original span is transient. This is the only allocation this filter
/// introduces.</para>
/// <para>Tokens are forwarded to the downstream sink unchanged — the filter is
/// transparent to the pipeline.</para>
/// </remarks>
public sealed class CachingTokenFilter : ISpanTokenFilter
{
    private readonly List<Token> _tokens = [];

    /// <summary>
    /// The tokens captured during the most recent <c>Analyse</c> call.
    /// Safe to enumerate multiple times without re-running the pipeline.
    /// </summary>
    public IReadOnlyList<Token> Tokens => _tokens;

    /// <summary>
    /// Clears the captured token list so the filter is ready for the next
    /// <c>Analyse</c> call.
    /// </summary>
    public void Reset()
    {
        _tokens.Clear();
    }

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
        ArgumentNullException.ThrowIfNull(sink);

        // Materialise the span into a string — we must own the data because the
        // caller may inspect Tokens long after the pipeline has finished.
        string capturedText = text.ToString();

        _tokens.Add(new Token(
            capturedText,
            startOffset,
            endOffset,
            type,
            positionIncrement,
            payload));

        // Forward unchanged to the next stage.
        sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Does not clear captured tokens. Call <see cref="Reset"/> explicitly
    /// when you are done reading <see cref="Tokens"/> and ready for the next
    /// analysis pass. Automatic reset on Finish would defeat the purpose of
    /// this filter, which exists to be inspected after analysis completes.
    /// </remarks>
    public void Finish(ISpanTokenSink sink)
    {
        // No-op: tokens are kept for inspection. Call Reset() explicitly.
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Returns <c>this</c> — cloning would defeat the purpose of this filter,
    /// which exists to be inspected after analysis. Callers that need a fresh
    /// capture should create a new <see cref="CachingTokenFilter"/> directly.
    /// </remarks>
    public ISpanTokenFilter Clone() => this;
}
