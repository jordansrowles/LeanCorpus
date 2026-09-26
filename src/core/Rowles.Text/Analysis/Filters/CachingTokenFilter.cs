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
/// the original span is transient.</para>
/// <para>Tokens are forwarded to the downstream sink unchanged — the filter is
/// transparent to the pipeline.</para>
/// </remarks>
public sealed class CachingTokenFilter : ISpanTokenFilter, IAnalysisContextFilter
{
    private readonly CachingTokenFilter? _publisher;
    private readonly List<Token> _tokens = [];
    private readonly List<Token> _executionTokens = [];
    private IReadOnlyList<Token>? _publishedTokens;

    /// <summary>Initialises an empty token cache.</summary>
    public CachingTokenFilter()
    {
    }

    private CachingTokenFilter(CachingTokenFilter publisher)
    {
        _publisher = publisher;
    }

    /// <summary>
    /// The tokens captured during the most recent <c>Analyse</c> call.
    /// Safe to enumerate multiple times without re-running the pipeline.
    /// </summary>
    public IReadOnlyList<Token> Tokens => _publisher is null
        ? Volatile.Read(ref _publishedTokens) ?? _tokens
        : _executionTokens;

    /// <summary>
    /// Clears the captured token list so the filter is ready for the next
    /// <c>Analyse</c> call.
    /// </summary>
    public void Reset()
    {
        if (_publisher is null)
        {
            _tokens.Clear();
            Volatile.Write(ref _publishedTokens, null);
        }
        else
            _executionTokens.Clear();
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
        => Apply(text, startOffset, endOffset, type, positionIncrement, 1, payload, sink);

    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        int positionLength,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        Token.ValidatePositionLength(positionLength);

        // Materialise the span into a string — we must own the data because the
        // caller may inspect Tokens long after the pipeline has finished.
        string capturedText = text.ToString();

        var token = new Token(
            capturedText,
            startOffset,
            endOffset,
            type,
            positionIncrement,
            payload,
            positionLength);
        if (_publisher is null)
        {
            if (Volatile.Read(ref _publishedTokens) is not null)
            {
                _tokens.Clear();
                Volatile.Write(ref _publishedTokens, null);
            }
            _tokens.Add(token);
        }
        else
            _executionTokens.Add(token);

        // Forward unchanged to the next stage.
        sink.Add(text, startOffset, endOffset, type, positionIncrement, positionLength, payload);
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
    /// Returns a new cache with independent capture state.
    /// </remarks>
    public ISpanTokenFilter Clone() => new CachingTokenFilter();

    ISpanTokenFilter IAnalysisContextFilter.CreateExecutionFilter()
        => new CachingTokenFilter(_publisher ?? this);

    void IAnalysisContextFilter.CompleteAnalysis()
    {
        if (_publisher is not null)
            Volatile.Write(ref _publisher._publishedTokens, _executionTokens.ToArray());
    }
}
