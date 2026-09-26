namespace Rowles.LeanCorpus.Analysis;

/// <summary>
/// Transforms span-backed tokens without requiring token text strings.
/// </summary>
public interface ISpanTokenFilter
{
    /// <summary>
    /// Applies the filter to a token and emits the transformed token into <paramref name="sink"/>.
    /// </summary>
    /// <param name="text">The token text span. Implementations must not retain this span after the call returns.</param>
    /// <param name="startOffset">The start character offset in the original input.</param>
    /// <param name="endOffset">The exclusive end character offset in the original input.</param>
    /// <param name="type">The token type.</param>
    /// <param name="positionIncrement">The position increment relative to the previous emitted token.</param>
    /// <param name="payload">The optional payload bytes for this token.</param>
    /// <param name="sink">The next sink in the analysis pipeline.</param>
    void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink);

    /// <summary>
    /// Applies the filter to a token edge with an explicit position length.
    /// </summary>
    /// <remarks>
    /// Legacy filters running inside an analysis context use its invocation-local sink
    /// to preserve the input edge length without allocating an adapter per token. Direct
    /// callers with another sink use a compatibility adapter when the edge is not unit
    /// length. Graph-producing filters must override this member when they create edges.
    /// </remarks>
    void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        int positionLength,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        Token.ValidatePositionLength(positionLength);

        if (sink is IPositionLengthContextSink positionLengthSink)
        {
            int previousPositionLength = positionLengthSink.PositionLength;
            positionLengthSink.PositionLength = positionLength;
            try
            {
                Apply(text, startOffset, endOffset, type, positionIncrement, payload, sink);
            }
            finally
            {
                positionLengthSink.PositionLength = previousPositionLength;
            }

            return;
        }

        if (positionLength == 1)
        {
            Apply(text, startOffset, endOffset, type, positionIncrement, payload, sink);
            return;
        }

        Apply(text, startOffset, endOffset, type, positionIncrement, payload,
            new PositionLengthForwardingSink(sink, positionLength));
    }

    /// <summary>
    /// Called after all tokens have been processed, allowing stateful filters to flush
    /// buffered tokens into the pipeline. The default implementation is a no-op.
    /// </summary>
    /// <param name="sink">The next sink in the analysis pipeline.</param>
    void Finish(ISpanTokenSink sink)
    {
        // Default no-op for stateless filters.
    }

    /// <summary>
    /// Creates an independent copy of this filter with the same configuration
    /// but fresh state. The default returns <c>this</c>, which is safe for
    /// stateless filters. Stateful filters must override to return a new instance.
    /// <see cref="Analysers.Analyser"/> uses this contract to create isolated filter
    /// state for each analysis call.
    /// </summary>
    ISpanTokenFilter Clone() => this;
}

/// <summary>Receives successful completion for a per-analysis filter clone.</summary>
internal interface IAnalysisContextFilter
{
    ISpanTokenFilter CreateExecutionFilter();

    void CompleteAnalysis();
}
