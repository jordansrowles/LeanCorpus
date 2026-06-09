using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Analysis.Filters;

/// <summary>
/// Applies an <see cref="IStemmer"/> to each token in the list.
/// Useful as a drop-in filter in the composable <see cref="Analysers.Analyser"/> pipeline.
/// </summary>
public sealed class StemTokenFilter : ISpanTokenFilter
{
    private readonly IStemmer _stemmer;

    /// <summary>
    /// Initialises a new <see cref="StemTokenFilter"/>.
    /// </summary>
    public StemTokenFilter(IStemmer stemmer)
    {
        _stemmer = stemmer ?? throw new ArgumentNullException(nameof(stemmer));
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
        var stemmed = _stemmer.Stem(new string(text));
        sink.Add(stemmed.AsSpan(), startOffset, endOffset, type, positionIncrement, payload);
    }

}
