namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Default analyser combining tokenisation, lowercase normalisation,
/// and stop-word removal into a single pipeline. Uses original input offsets
/// for lowercasing to avoid double string allocation.
/// The returned token list is reused across calls — callers must not hold
/// references to it beyond the current invocation.
/// 
/// Thread-safety: This class maintains instance-level buffers (_tokensBuf, _lowerBuf, _internCache)
/// for performance. Each instance should be used by a single thread, or callers should create
/// separate instances per thread (as IndexWriter does in AddDocumentsConcurrent).
/// </summary>
public sealed class StandardAnalyser : IAnalyser
{
    private readonly Tokeniser _tokeniser = new();
    private readonly StopWordFilter _stopWordFilter;
    private char[] _lowerBuf = new char[64];
    private readonly List<(int Start, int End)> _offsetBuf = new();
    private readonly List<Token> _tokensBuf = new();
    // Intern cache keyed by string value to avoid hash-collision misses.
    // Uses AlternateLookup<ReadOnlySpan<char>> for zero-alloc cache hits.
    private readonly Dictionary<string, string> _internCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> _internLookup;
    private readonly int _maxInternCacheSize;

    /// <summary>
    /// Initialises a new <see cref="StandardAnalyser"/> with the specified intern cache size and stop words.
    /// </summary>
    /// <param name="internCacheSize">Maximum number of entries in the internal token string cache. Defaults to 4096.</param>
    /// <param name="stopWords">Custom stop word list, or <see langword="null"/> to use the built-in English list.</param>
    public StandardAnalyser(int internCacheSize = 4096, IEnumerable<string>? stopWords = null)
    {
        _maxInternCacheSize = internCacheSize;
        _stopWordFilter = new StopWordFilter(stopWords);
        _internLookup = _internCache.GetAlternateLookup<ReadOnlySpan<char>>();
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
            var span = input.Slice(start, end - start);

            if (_stopWordFilter.IsStopWord(span))
                continue;

            int len = end - start;
            if (len > _lowerBuf.Length)
                _lowerBuf = new char[Math.Max(_lowerBuf.Length * 2, len)];

            span.ToLowerInvariant(_lowerBuf.AsSpan(0, len));
            var lowerSpan = _lowerBuf.AsSpan(0, len);

            // Try intern cache to reuse string objects for repeated tokens
            string text;
            if (_internLookup.TryGetValue(lowerSpan, out var cached))
            {
                text = cached;
            }
            else
            {
                text = new string(lowerSpan);
                if (_internCache.Count < _maxInternCacheSize)
                    _internLookup[lowerSpan] = text;
            }

            _tokensBuf.Add(new Token(text, start, end));
        }

        return _tokensBuf;
    }
}
