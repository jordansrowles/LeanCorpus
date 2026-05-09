namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Analyser that splits text into letter-only tokens and lowercases them without stop-word removal.
/// The returned token list is reused across calls; callers must not hold
/// references to it beyond the current invocation.
///
/// Thread-safety: This class maintains instance-level buffers for performance.
/// Each instance should be used by a single thread, or callers should create
/// separate instances per thread.
/// </summary>
public sealed class SimpleAnalyser : IAnalyser
{
    private readonly LetterTokeniser _tokeniser = new();
    private readonly List<(int Start, int End)> _offsetBuf = new();
    private readonly List<Token> _tokensBuf = new();
    private readonly TokenTextCache _internCache;
    private char[] _lowerBuf = new char[64];

    /// <summary>
    /// Initialises a new <see cref="SimpleAnalyser"/>.
    /// </summary>
    /// <param name="internCacheSize">Maximum number of token strings retained for reuse. Defaults to 4096.</param>
    public SimpleAnalyser(int internCacheSize = 4096)
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
            int length = end - start;
            if (length > _lowerBuf.Length)
                _lowerBuf = new char[Math.Max(_lowerBuf.Length * 2, length)];

            input.Slice(start, length).ToLowerInvariant(_lowerBuf.AsSpan(0, length));
            string text = _internCache.GetOrAdd(_lowerBuf.AsSpan(0, length));
            _tokensBuf.Add(new Token(text, start, end));
        }

        return _tokensBuf;
    }
}
