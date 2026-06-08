using Rowles.LeanCorpus.Codecs.TermVectors;

namespace Rowles.LeanCorpus.Search.Highlighting;

/// <summary>
/// Unified contract for extracting highlighted text fragments from stored fields.
/// </summary>
public interface IHighlighter
{
    /// <summary>
    /// Returns the best snippet from <paramref name="text"/> containing highlighted
    /// occurrences of the query terms.
    /// </summary>
    /// <param name="text">The stored field text to highlight.</param>
    /// <param name="query">The query whose terms to highlight.</param>
    /// <param name="termVectors">
    /// Optional term vector entries for the document and field. When provided and
    /// containing offsets, implementations may use them to avoid re-analysis.
    /// </param>
    /// <param name="maxSnippetLength">Maximum character length of the returned snippet.</param>
    /// <returns>A snippet of <paramref name="text"/> with matching terms wrapped in highlight tags.</returns>
    string GetBestFragment(string text, Query query,
        IReadOnlyList<TermVectorEntry>? termVectors = null,
        int maxSnippetLength = 200);
}
