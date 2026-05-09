namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Analyser that splits text only on whitespace and applies no token filters.
/// The returned token list is reused across calls; callers must not hold
/// references to it beyond the current invocation.
///
/// Thread-safety: This class maintains instance-level buffers for performance.
/// Each instance should be used by a single thread, or callers should create
/// separate instances per thread.
/// </summary>
public sealed class WhitespaceAnalyser : IAnalyser
{
    private readonly WhitespaceTokeniser _tokeniser = new();
    private readonly List<(int Start, int End)> _offsetBuf = new();
    private readonly List<Token> _tokensBuf = new();
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
    public List<Token> Analyse(ReadOnlySpan<char> input)
    {
        _tokeniser.TokeniseOffsets(input, _offsetBuf);

        _tokensBuf.Clear();
        if (_tokensBuf.Capacity < _offsetBuf.Count)
            _tokensBuf.Capacity = _offsetBuf.Count;

        for (int i = 0; i < _offsetBuf.Count; i++)
        {
            var (start, end) = _offsetBuf[i];
            string text = _internCache.GetOrAdd(input.Slice(start, end - start));
            _tokensBuf.Add(new Token(text, start, end));
        }

        return _tokensBuf;
    }
}
