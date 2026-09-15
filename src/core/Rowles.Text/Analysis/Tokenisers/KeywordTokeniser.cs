namespace Rowles.LeanCorpus.Analysis.Tokenisers;

/// <summary>
/// Treats the complete input as a single token.
/// </summary>
public sealed class KeywordTokeniser : IShareableSpanTokeniser
{
    /// <inheritdoc/>
    public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        if (!input.IsEmpty)
            sink.Add(input, 0, input.Length);
    }
}
