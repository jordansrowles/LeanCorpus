using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Codecs.TermVectors;

namespace Rowles.LeanCorpus.Search.Highlighting;

/// <summary>
/// Cascading highlighter that selects the best strategy based on available data.
/// </summary>
/// <remarks>
/// <para>
/// When term vectors with character offsets are available, delegates to
/// <see cref="TermVectorHighlighter"/> for phrase-aware, boundary-scanned
/// highlighting without re-analysis.
/// </para>
/// <para>
/// Otherwise falls back to <see cref="Highlighter"/>, which re-analyses the
/// stored text with a <see cref="StandardAnalyser"/>.
/// </para>
/// </remarks>
public sealed class HybridHighlighter : IHighlighter
{
    private readonly TermVectorHighlighter _termVectorHighlighter;
    private readonly Highlighter _highlighter;

    /// <summary>
    /// Initialises a new <see cref="HybridHighlighter"/> with the given highlight
    /// tags and optional fallback analyser.
    /// </summary>
    /// <param name="preTag">Opening highlight tag, e.g. "&lt;b&gt;" or "&lt;em&gt;".</param>
    /// <param name="postTag">Closing highlight tag, e.g. "&lt;/b&gt;" or "&lt;/em&gt;".</param>
    /// <param name="fallbackAnalyser">
    /// Analyser used when falling back to <see cref="Highlighter"/>.
    /// Defaults to <see cref="StandardAnalyser"/>.
    /// </param>
    public HybridHighlighter(string preTag = "<b>", string postTag = "</b>",
        IAnalyser? fallbackAnalyser = null)
    {
        _termVectorHighlighter = new TermVectorHighlighter(preTag, postTag);
        _highlighter = new Highlighter(preTag, postTag, fallbackAnalyser);
    }

    /// <inheritdoc />
    public string GetBestFragment(string text, Query query,
        IReadOnlyList<TermVectorEntry>? termVectors = null,
        int maxSnippetLength = 200)
    {
        if (termVectors is { Count: > 0 } && HasOffsets(termVectors))
            return _termVectorHighlighter.GetBestFragment(text, query, termVectors, maxSnippetLength);
        return _highlighter.GetBestFragment(text, query, termVectors, maxSnippetLength);
    }

    private static bool HasOffsets(IReadOnlyList<TermVectorEntry> tvs)
    {
        foreach (var tv in tvs)
            if (tv.StartOffsets is not null && tv.EndOffsets is not null)
                return true;
        return false;
    }
}
