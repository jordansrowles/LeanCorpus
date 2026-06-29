namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Analyser that splits text only on whitespace and applies no token filters.
/// </summary>
public sealed class WhitespaceAnalyser : IAnalyser
{
    private readonly WhitespaceTokeniser _tokeniser = new();
    private readonly List<(int Start, int End)> _offsetBuf = new();
    private readonly TokenTextCache _internCache;

    /// <summary>
    /// Initialises a new <see cref="WhitespaceAnalyser"/>.
    /// </summary>
    /// <param name="internCacheSize">Maximum number of token strings retained for reuse. Defaults to 4096.</param>
    public WhitespaceAnalyser(int internCacheSize = 4096)
    {
        _internCache = new TokenTextCache(internCacheSize);
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        _tokeniser.TokeniseOffsets(input, _offsetBuf);

        for (int i = 0; i < _offsetBuf.Count; i++)
        {
            var (start, end) = _offsetBuf[i];
            string text = _internCache.GetOrAdd(input.Slice(start, end - start));
            sink.Add(text.AsSpan(), start, end);
        }
    }
}
