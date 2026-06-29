namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Analyser that treats the complete input as a single token.
/// </summary>
public sealed class KeywordAnalyser : IAnalyser
{
    private readonly TokenTextCache _internCache;

    /// <summary>
    /// Initialises a new <see cref="KeywordAnalyser"/>.
    /// </summary>
    /// <param name="internCacheSize">Maximum number of token strings retained for reuse. Defaults to 4096.</param>
    public KeywordAnalyser(int internCacheSize = 4096)
    {
        _internCache = new TokenTextCache(internCacheSize);
    }
    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        if (!input.IsEmpty)
            sink.Add(_internCache.GetOrAdd(input).AsSpan(), 0, input.Length);
    }

}
