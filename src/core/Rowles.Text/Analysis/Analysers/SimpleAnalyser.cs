namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Analyser that splits text into letter-only tokens and lowercases them without stop-word removal.
/// </summary>
public sealed class SimpleAnalyser : IAnalyser
{
    private readonly LetterTokeniser _tokeniser = new();
    private readonly List<(int Start, int End)> _offsetBuf = new();
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
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        _tokeniser.TokeniseOffsets(input, _offsetBuf);

        for (int i = 0; i < _offsetBuf.Count; i++)
        {
            var (start, end) = _offsetBuf[i];
            int length = end - start;
            if (length > _lowerBuf.Length)
                _lowerBuf = new char[Math.Max(_lowerBuf.Length * 2, length)];

            input.Slice(start, length).ToLowerInvariant(_lowerBuf.AsSpan(0, length));
            var lowerSpan = _lowerBuf.AsSpan(0, length);
            string text = _internCache.GetOrAdd(lowerSpan);
            sink.Add(text.AsSpan(), start, end);
        }
    }
}
