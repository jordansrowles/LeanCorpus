namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Default analyser combining tokenisation, lowercase normalisation,
/// and stop-word removal into a single pipeline. Passes span-backed tokens
/// directly to the sink without per-token string allocations.
///
/// Thread-safety: This class maintains instance-level buffers (_lowerBuf, _offsetBuf)
/// for performance. Each instance should be used by a single thread, or callers should create
/// separate instances per thread (as IndexWriter does in AddDocumentsConcurrent).
/// </summary>
public sealed class StandardAnalyser : IAnalyser
{
    private readonly Tokeniser _tokeniser = new();
    private readonly StopWordFilter _stopWordFilter;
    private char[] _lowerBuf = new char[64];
    private readonly List<(int Start, int End)> _offsetBuf = new();

    /// <summary>
    /// Initialises a new <see cref="StandardAnalyser"/> with the specified stop words.
    /// </summary>
    /// <param name="internCacheSize">Ignored; retained for binary compatibility. Token string interning has been removed — the analyser now passes spans directly to the sink.</param>
    /// <param name="stopWords">Custom stop word list, or <see langword="null"/> to use the built-in English list.</param>
    public StandardAnalyser(int internCacheSize = 4096, IEnumerable<string>? stopWords = null)
    {
        _ = internCacheSize; // retained for source compatibility, no longer used
        _stopWordFilter = new StopWordFilter(stopWords);
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        _tokeniser.TokeniseOffsets(input, _offsetBuf);

        for (int i = 0; i < _offsetBuf.Count; i++)
        {
            var (start, end) = _offsetBuf[i];
            var span = input.Slice(start, end - start);

            if (_stopWordFilter.IsStopWord(span))
                continue;

            int len = end - start;
            if (len > _lowerBuf.Length)
                _lowerBuf = new char[Math.Max(_lowerBuf.Length * 2, len)];

            AsciiCharInspector.AsciiToLower(span, _lowerBuf.AsSpan(0, len));
            var lowerSpan = _lowerBuf.AsSpan(0, len);

            sink.Add(lowerSpan, start, end);
        }
    }

}
