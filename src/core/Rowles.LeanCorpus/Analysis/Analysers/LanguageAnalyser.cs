using System.Buffers;
namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Configurable analyser that chains a tokeniser, lowercase normalisation,
/// stop-word removal, and optional stemming. Used by <see cref="AnalyserFactory"/>
/// for language-specific analysis pipelines.
/// </summary>
/// <remarks>
/// Instances are safe to share across threads provided the supplied
    /// <see cref="ISpanTokeniser"/> and <see cref="IStemmer"/> are also thread-safe.
/// Per-call scratch buffers are rented from <see cref="ArrayPool{T}"/> and
/// returned before the method exits.
/// </remarks>
public sealed class LanguageAnalyser : IAnalyser
{
    private readonly ISpanTokeniser _tokeniser;
    private readonly StopWordFilter _stopWordFilter;
    private readonly IStemmer? _stemmer;
    private readonly KeywordMarkerFilter? _keywordMarker;

    /// <summary>
    /// Initialises a new <see cref="LanguageAnalyser"/> with the specified tokeniser, stop words, and optional stemmer.
    /// </summary>
    /// <param name="tokeniser">The tokeniser used to split input text into raw tokens.</param>
    /// <param name="stopWords">Stop words to remove, or <see langword="null"/> to use the default English list.</param>
    /// <param name="stemmer">Optional stemmer to reduce tokens to their root form, or <see langword="null"/> to skip stemming.</param>
    /// <param name="keywordMarker">Optional keyword marker used to skip stemming for selected token text.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="tokeniser"/> is <see langword="null"/>.</exception>
    public LanguageAnalyser(ISpanTokeniser tokeniser, IEnumerable<string>? stopWords, IStemmer? stemmer, KeywordMarkerFilter? keywordMarker = null)
    {
        _tokeniser = tokeniser ?? throw new ArgumentNullException(nameof(tokeniser));
        _stopWordFilter = new StopWordFilter(stopWords);
        _stemmer = stemmer;
        _keywordMarker = keywordMarker;
    }

    /// <inheritdoc/>
    public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var matSink = new MaterialisingTokenSink();
        _tokeniser.Tokenise(input, matSink);
        var rawTokens = matSink.Tokens;

        const int StackThreshold = 128;
        char[]? rented = null;
        try
        {
            for (int i = 0; i < rawTokens.Count; i++)
            {
                var t = rawTokens[i];
                var span = t.Text.AsSpan();
                int len = span.Length;

                if (len <= StackThreshold)
                {
#pragma warning disable CA2014 // stackalloc in loop bounded by token count; ≤128 chars per allocation
                    Span<char> workBuf = stackalloc char[len];
#pragma warning restore CA2014
                    span.ToLowerInvariant(workBuf);
                    string text = new string(workBuf);

                    if (_stopWordFilter.IsStopWord(text))
                        continue;

                    if (_stemmer is not null && (_keywordMarker is null || !_keywordMarker.IsKeyword(text)))
                        text = _stemmer.Stem(text);

                    sink.Add(text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload);
                }
                else
                {
                    if (rented is null || rented.Length < len)
                    {
                        if (rented is not null) ArrayPool<char>.Shared.Return(rented);
                        rented = ArrayPool<char>.Shared.Rent(Math.Max(len, 64));
                    }

                    span.ToLowerInvariant(rented.AsSpan(0, len));
                    string text = new string(rented, 0, len);

                    if (_stopWordFilter.IsStopWord(text))
                        continue;

                    if (_stemmer is not null && (_keywordMarker is null || !_keywordMarker.IsKeyword(text)))
                        text = _stemmer.Stem(text);

                    sink.Add(text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload);
                }
            }
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }
}
