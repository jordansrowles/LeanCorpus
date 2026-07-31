namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// An <see cref="ISpanTokenSink"/> that counts accepted tokens without allocating any strings or Token objects.
/// Use for performance measurement of the analysis pipeline when only throughput matters, not the tokens themselves.
/// </summary>
public sealed class CountingTokenSink : ISpanTokenSink
{
    /// <summary>Gets the number of tokens accepted since the last <see cref="Reset"/>.</summary>
    public int Count { get; private set; }

    /// <summary>Resets the token count to zero.</summary>
    public void Reset() => Count = 0;

    /// <inheritdoc/>
    public void Add(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type = Token.DefaultType,
        int positionIncrement = 1,
        byte[]? payload = null)
    {
        Count++;
    }
}
