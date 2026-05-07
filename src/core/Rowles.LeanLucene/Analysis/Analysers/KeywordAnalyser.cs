namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Analyser that treats the complete input as a single token.
/// The returned token list is reused across calls; callers must not hold
/// references to it beyond the current invocation.
///
/// Thread-safety: This class maintains instance-level buffers for performance.
/// Each instance should be used by a single thread, or callers should create
/// separate instances per thread.
/// </summary>
public sealed class KeywordAnalyser : IAnalyser
{
    private readonly List<Token> _tokensBuf = new(1);
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
    public List<Token> Analyse(ReadOnlySpan<char> input)
    {
        _tokensBuf.Clear();
        if (!input.IsEmpty)
            _tokensBuf.Add(new Token(_internCache.GetOrAdd(input), 0, input.Length));

        return _tokensBuf;
    }
}
