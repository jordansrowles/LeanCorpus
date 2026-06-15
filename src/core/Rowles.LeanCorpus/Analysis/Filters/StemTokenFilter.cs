using System.Buffers;
using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Applies an <see cref="ISpanStemmer"/> to each token in the list.
/// Useful as a drop-in filter in the composable <see cref="Analysers.Analyser"/> pipeline.
/// </summary>
public sealed class StemTokenFilter : ISpanTokenFilter
{
    private readonly ISpanStemmer _stemmer;
    private readonly KeywordMarkerFilter? _keywordMarker;

    /// <summary>
    /// Initialises a new <see cref="StemTokenFilter"/> that stems all tokens.
    /// </summary>
    public StemTokenFilter(ISpanStemmer stemmer)
    {
        _stemmer = stemmer ?? throw new ArgumentNullException(nameof(stemmer));
    }

    /// <summary>
    /// Initialises a new <see cref="StemTokenFilter"/> with an optional keyword marker.
    /// Tokens recognised by <paramref name="keywordMarker"/> are passed through unchanged.
    /// </summary>
    public StemTokenFilter(ISpanStemmer stemmer, KeywordMarkerFilter? keywordMarker)
    {
        _stemmer = stemmer ?? throw new ArgumentNullException(nameof(stemmer));
        _keywordMarker = keywordMarker;
    }

    /// <inheritdoc/>
    public void Apply(
        ReadOnlySpan<char> text,
        int startOffset,
        int endOffset,
        string type,
        int positionIncrement,
        byte[]? payload,
        ISpanTokenSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (_keywordMarker is not null && _keywordMarker.IsKeyword(text))
        {
            sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            return;
        }

        // Fast pre-filter: stemming suffixes all end in one of these characters.
        // ~85% of tokens don't — skip buffer allocation for them entirely.
        if (text.Length > 0)
        {
            char last = text[text.Length - 1];
            if (last is not ('s' or 'd' or 'g' or 'r' or 'y' or 't' or 'l' or 'e' or 'c' or 'm' or 'p'))
            {
                sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
                return;
            }
        }

        const int StackThreshold = 128;
        char[]? rented = null;
        try
        {
            int bufSize = text.Length + 1; // +1 for rare expansion cases (e.g. German ß→ss)
            Span<char> buf = bufSize <= StackThreshold
                ? stackalloc char[bufSize]
                : (rented = ArrayPool<char>.Shared.Rent(bufSize)).AsSpan(0, bufSize);

            // Use StemPreLowered when available to skip the redundant ToLowerInvariant loop.
            var ks = _stemmer as KStemmer;
            int len = ks is not null
                ? ks.StemPreLowered(text, buf)
                : _stemmer.Stem(text, buf);

            if (len < 0)
            {
                // Buffer too small (e.g. multiple ß→ss expansions). Rent larger.
                if (rented is not null) { ArrayPool<char>.Shared.Return(rented); rented = null; }
                bufSize = text.Length * 2;
                rented = ArrayPool<char>.Shared.Rent(bufSize);
                buf = rented.AsSpan(0, bufSize);
                len = ks is not null
                    ? ks.StemPreLowered(text, buf)
                    : _stemmer.Stem(text, buf);
            }

            // When len == text.Length with KStemmer, the word was explicitly
            // returned unchanged — skip the redundant SequenceEqual scan.
            if (len == text.Length && ks is not null)
            {
                sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            }
            else if (len == text.Length && buf[..len].SequenceEqual(text))
            {
                sink.Add(text, startOffset, endOffset, type, positionIncrement, payload);
            }
            else
            {
                sink.Add(buf[..len], startOffset, endOffset, type, positionIncrement, payload);
            }
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

}
