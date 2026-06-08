using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Search.Highlighting;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Highlighting;

public sealed class HybridHighlighterTests
{
    private readonly HybridHighlighter _hybrid = new("<b>", "</b>");

    [Fact]
    public void GetBestFragment_WithOffsets_DelegatesToTermVectorHighlighter()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5])
        };
        var query = new TermQuery("body", "hello");

        string result = _hybrid.GetBestFragment("hello world", query, termVectors);

        Assert.Equal("<b>hello</b> world", result);
    }

    [Fact]
    public void GetBestFragment_WithoutOffsets_FallsBackToHighlighter()
    {
        // Term vectors without offsets → Highlighter re-analyses.
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0]) // no offsets
        };
        var query = new TermQuery("body", "hello");

        string result = _hybrid.GetBestFragment("hello world", query, termVectors);

        Assert.Contains("<b>hello</b>", result);
    }

    [Fact]
    public void GetBestFragment_NullTermVectors_FallsBackToHighlighter()
    {
        var query = new TermQuery("body", "hello");

        string result = _hybrid.GetBestFragment("hello world", query, termVectors: null);

        Assert.Contains("<b>hello</b>", result);
    }

    [Fact]
    public void GetBestFragment_EmptyTermVectors_FallsBackToHighlighter()
    {
        var termVectors = new List<TermVectorEntry>();
        var query = new TermQuery("body", "hello");

        string result = _hybrid.GetBestFragment("hello world", query, termVectors);

        Assert.Contains("<b>hello</b>", result);
    }

    [Fact]
    public void GetBestFragment_NoMatch_ReturnsTruncatedText()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5])
        };
        var query = new TermQuery("body", "missing");

        string result = _hybrid.GetBestFragment("hello world", query, termVectors);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void GetBestFragment_PhraseWithOffsets_HighlightsPhraseTerm()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("search", 1, [3], StartOffsets: [14], EndOffsets: [20]),
            new("engine", 1, [4], StartOffsets: [21], EndOffsets: [27])
        };
        var query = new PhraseQuery("body", "search", "engine");

        string result = _hybrid.GetBestFragment(
            "this is about search engine technology", query, termVectors);

        Assert.Contains("<b>search</b>", result);
        Assert.Contains("<b>engine</b>", result);
    }
}
