using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Search.Highlighting;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Highlighting;

public sealed class PostingsHighlighterTests
{
    private readonly PostingsHighlighter _highlighter = new("<b>", "</b>");

    [Fact]
    public void GetBestFragment_HighlightsMatchingTerms()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5]),
            new("world", 1, [1], StartOffsets: [6], EndOffsets: [11])
        };
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hello" };

        string result = _highlighter.GetBestFragment("hello world", termVectors, queryTerms);

        Assert.Equal("<b>hello</b> world", result);
    }

    [Fact]
    public void GetBestFragment_HighlightsMultipleTerms()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5]),
            new("world", 1, [1], StartOffsets: [6], EndOffsets: [11])
        };
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hello", "world" };

        string result = _highlighter.GetBestFragment("hello world", termVectors, queryTerms);

        Assert.Equal("<b>hello</b> <b>world</b>", result);
    }

    [Fact]
    public void GetBestFragment_NoMatches_ReturnsTruncatedText()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5])
        };
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "missing" };

        string result = _highlighter.GetBestFragment("hello world", termVectors, queryTerms);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void GetBestFragment_EmptyText_ReturnsEmpty()
    {
        var termVectors = new List<TermVectorEntry>();
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test" };

        string result = _highlighter.GetBestFragment("", termVectors, queryTerms);

        Assert.Equal("", result);
    }

    [Fact]
    public void GetBestFragment_EmptyTermVectors_ReturnsTruncatedText()
    {
        var termVectors = new List<TermVectorEntry>();
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test" };

        string result = _highlighter.GetBestFragment("hello world", termVectors, queryTerms);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void GetBestFragment_TermVectorsWithoutOffsets_ReturnsTruncatedText()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0]) // no offsets
        };
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hello" };

        string result = _highlighter.GetBestFragment("hello world", termVectors, queryTerms);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void GetBestFragment_TruncatesLongText()
    {
        string text = new('x', 500);
        var termVectors = new List<TermVectorEntry>();
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "test" };

        string result = _highlighter.GetBestFragment(text, termVectors, queryTerms, maxSnippetLength: 100);

        Assert.Equal(103, result.Length); // 100 chars + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void GetBestFragment_AddsEllipsisAtEdges()
    {
        string padding = new('x', 100);
        string text = padding + "hello world" + padding;
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [100], EndOffsets: [105])
        };
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hello" };

        string result = _highlighter.GetBestFragment(text, termVectors, queryTerms, maxSnippetLength: 50);

        Assert.StartsWith("...", result);
        Assert.Contains("<b>hello</b>", result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void GetBestFragment_OverlappingOffsetWindows_Deduplicates()
    {
        // Two matches of the same term at different positions.
        var termVectors = new List<TermVectorEntry>
        {
            new("the", 2, [0, 2], StartOffsets: [0, 8], EndOffsets: [3, 11])
        };
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "the" };

        string result = _highlighter.GetBestFragment("the cat the dog", termVectors, queryTerms);

        Assert.Equal("<b>the</b> cat <b>the</b> dog", result);
    }

    [Fact]
    public void GetBestFragment_SkipsOffsetsOutsideText()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [50], EndOffsets: [55])
        };
        var queryTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "hello" };

        // Offset is beyond the text, should be skipped.
        string result = _highlighter.GetBestFragment("short", termVectors, queryTerms);

        Assert.Equal("short", result);
    }
}
