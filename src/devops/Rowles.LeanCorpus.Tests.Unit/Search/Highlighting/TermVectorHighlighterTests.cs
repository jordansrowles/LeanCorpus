using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Search.Highlighting;

namespace Rowles.LeanCorpus.Tests.Unit.Search.Highlighting;

// -- FieldQuery tests --

public sealed class FieldQueryTests
{
    [Fact]
    public void Constructor_TermQuery_FlattensSingleTerm()
    {
        var query = new TermQuery("title", "corpus");
        var fq = new FieldQuery(query);

        Assert.True(fq.HasField("title"));
        Assert.Contains("corpus", fq.GetTerms("title"));
        Assert.Null(fq.GetPhrasePositions("title", "corpus"));
    }

    [Fact]
    public void GetPhrasePositions_ReturnsNullForNonPhraseTerm()
    {
        var query = new TermQuery("body", "search");
        var fq = new FieldQuery(query);

        Assert.Null(fq.GetPhrasePositions("body", "search"));
    }

    [Fact]
    public void GetPhrasePositions_ReturnsPositionsForPhraseTerm()
    {
        var query = new PhraseQuery("title", "hello", "world");
        var fq = new FieldQuery(query);

        Assert.True(fq.HasField("title"));
        Assert.NotNull(fq.GetPhrasePositions("title", "hello"));
        Assert.Contains(0, fq.GetPhrasePositions("title", "hello")!);
        Assert.NotNull(fq.GetPhrasePositions("title", "world"));
        Assert.Contains(1, fq.GetPhrasePositions("title", "world")!);
    }

    [Fact]
    public void HasField_ReturnsFalseForUnknownField()
    {
        var query = new TermQuery("title", "corpus");
        var fq = new FieldQuery(query);

        Assert.False(fq.HasField("body"));
    }

    [Fact]
    public void Fields_EnumeratesAllFields()
    {
        var bq = new BooleanQuery.Builder()
            .Add(new TermQuery("title", "corpus"), Occur.Must)
            .Add(new TermQuery("body", "search"), Occur.Should)
            .Build();
        var fq = new FieldQuery(bq);

        var fields = fq.Fields.ToList();
        Assert.Contains("title", fields);
        Assert.Contains("body", fields);
    }

    [Fact]
    public void Flatten_BooleanQuery_MustNotExcluded()
    {
        var bq = new BooleanQuery.Builder()
            .Add(new TermQuery("title", "corpus"), Occur.Must)
            .Add(new TermQuery("title", "excluded"), Occur.MustNot)
            .Build();
        var fq = new FieldQuery(bq);

        Assert.Contains("corpus", fq.GetTerms("title"));
        Assert.DoesNotContain("excluded", fq.GetTerms("title"));
    }

    [Fact]
    public void Flatten_DisjunctionMax_UnionOfSubQueries()
    {
        var dmq = new DisjunctionMaxQuery()
            .Add(new TermQuery("title", "corpus"))
            .Add(new TermQuery("title", "search"))
            .Freeze();
        var fq = new FieldQuery(dmq);

        var terms = fq.GetTerms("title");
        Assert.Contains("corpus", terms);
        Assert.Contains("search", terms);
    }

    [Fact]
    public void Flatten_ConstantScore_DelegatesToInner()
    {
        var inner = new TermQuery("title", "corpus");
        var csq = new ConstantScoreQuery(inner);
        var fq = new FieldQuery(csq);

        Assert.True(fq.HasField("title"));
        Assert.Contains("corpus", fq.GetTerms("title"));
    }

    [Fact]
    public void Flatten_PrefixQuery_AnyPosition()
    {
        var query = new PrefixQuery("title", "cor");
        var fq = new FieldQuery(query);

        Assert.True(fq.HasField("title"));
        Assert.Contains("cor", fq.GetTerms("title"));
        Assert.Null(fq.GetPhrasePositions("title", "cor"));
    }

    [Fact]
    public void Flatten_FuzzyQuery_AnyPosition()
    {
        var query = new FuzzyQuery("title", "corpus");
        var fq = new FieldQuery(query);

        Assert.True(fq.HasField("title"));
        Assert.Contains("corpus", fq.GetTerms("title"));
        Assert.Null(fq.GetPhrasePositions("title", "corpus"));
    }

    [Fact]
    public void Flatten_WildcardQuery_AnyPosition()
    {
        var query = new WildcardQuery("title", "cor*");
        var fq = new FieldQuery(query);

        Assert.True(fq.HasField("title"));
        Assert.Contains("cor*", fq.GetTerms("title"));
        Assert.Null(fq.GetPhrasePositions("title", "cor*"));
    }

    [Fact]
    public void Flatten_TermInSet_MultipleTermsAtAnyPosition()
    {
        var query = new TermInSetQuery("title", "corpus", "search", "engine");
        var fq = new FieldQuery(query);

        var terms = fq.GetTerms("title");
        Assert.Contains("corpus", terms);
        Assert.Contains("search", terms);
        Assert.Contains("engine", terms);
        Assert.Null(fq.GetPhrasePositions("title", "corpus"));
    }

    [Fact]
    public void Flatten_MultiPhrase_PositionsWithAlternates()
    {
        // Slot 0: "hello" or "hi", Slot 1: "world" or "there"
        var mpq = new MultiPhraseQuery("title",
            [["hello", "hi"], ["world", "there"]]);
        var fq = new FieldQuery(mpq);

        Assert.NotNull(fq.GetPhrasePositions("title", "hello"));
        Assert.Contains(0, fq.GetPhrasePositions("title", "hello")!);
        Assert.Contains(0, fq.GetPhrasePositions("title", "hi")!);
        Assert.Contains(1, fq.GetPhrasePositions("title", "world")!);
        Assert.Contains(1, fq.GetPhrasePositions("title", "there")!);
    }

    [Fact]
    public void Flatten_PhraseAndTermMerge_NullsOutPositions()
    {
        // When the same term appears in both a PhraseQuery and a TermQuery,
        // positions should be nulled out (any position matches).
        var bq = new BooleanQuery.Builder()
            .Add(new PhraseQuery("title", "hello", "world"), Occur.Must)
            .Add(new TermQuery("title", "hello"), Occur.Should)
            .Build();
        var fq = new FieldQuery(bq);

        // "hello" appears in both phrase and term context → positions null
        Assert.Null(fq.GetPhrasePositions("title", "hello"));
        // "world" only appears in phrase context → has positions
        Assert.NotNull(fq.GetPhrasePositions("title", "world"));
        Assert.Contains(1, fq.GetPhrasePositions("title", "world")!);
    }
}

// -- TermVectorHighlighter tests --

public sealed class TermVectorHighlighterTests
{
    private readonly TermVectorHighlighter _highlighter = new("<b>", "</b>");

    [Fact]
    public void GetBestFragment_HighlightsSingleTerm()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5])
        };
        var fq = new FieldQuery(new TermQuery("body", "hello"));

        string result = _highlighter.GetBestFragment("hello world", fq, termVectors);

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
        var bq = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "hello"), Occur.Must)
            .Add(new TermQuery("body", "world"), Occur.Must)
            .Build();
        var fq = new FieldQuery(bq);

        string result = _highlighter.GetBestFragment("hello world", fq, termVectors);

        Assert.Equal("<b>hello</b> <b>world</b>", result);
    }

    [Fact]
    public void GetBestFragment_NoMatches_ReturnsTruncatedText()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5])
        };
        var fq = new FieldQuery(new TermQuery("body", "missing"));

        string result = _highlighter.GetBestFragment("hello world", fq, termVectors);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void GetBestFragment_EmptyText_ReturnsEmpty()
    {
        var termVectors = new List<TermVectorEntry>();
        var fq = new FieldQuery(new TermQuery("body", "test"));

        string result = _highlighter.GetBestFragment("", fq, termVectors);

        Assert.Equal("", result);
    }

    [Fact]
    public void GetBestFragment_EmptyTermVectors_ReturnsTruncatedText()
    {
        var termVectors = new List<TermVectorEntry>();
        var fq = new FieldQuery(new TermQuery("body", "test"));

        string result = _highlighter.GetBestFragment("hello world", fq, termVectors);

        Assert.Equal("hello world", result);
    }

    [Fact]
    public void GetBestFragment_TermVectorsWithoutOffsets_FallsBack()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0]) // no offsets — triggers fallback
        };
        var fq = new FieldQuery(new TermQuery("body", "hello"));

        // Fallback uses StandardAnalyser to re-analyse and match.
        string result = _highlighter.GetBestFragment("hello world", fq, termVectors);

        Assert.Contains("<b>hello</b>", result);
    }

    [Fact]
    public void GetBestFragment_TruncatesLongText()
    {
        string text = new('x', 500);
        var termVectors = new List<TermVectorEntry>();
        var fq = new FieldQuery(new TermQuery("body", "test"));

        string result = _highlighter.GetBestFragment(text, fq, termVectors, maxSnippetLength: 100);

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
        var fq = new FieldQuery(new TermQuery("body", "hello"));

        string result = _highlighter.GetBestFragment(text, fq, termVectors, maxSnippetLength: 50);

        Assert.StartsWith("...", result);
        Assert.Contains("<b>hello</b>", result);
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void GetBestFragment_PhraseTerms_FilteredByAdjacency()
    {
        // "search engine" as a phrase. Term vectors have both terms at adjacent positions.
        var termVectors = new List<TermVectorEntry>
        {
            new("search", 1, [3], StartOffsets: [14], EndOffsets: [20]),
            new("engine", 1, [4], StartOffsets: [21], EndOffsets: [27])
        };
        var fq = new FieldQuery(new PhraseQuery("body", "search", "engine"));

        string result = _highlighter.GetBestFragment("this is about search engine technology", fq, termVectors);

        // Both terms are adjacent in the term vector → both highlighted.
        Assert.Contains("<b>search</b>", result);
        Assert.Contains("<b>engine</b>", result);
    }

    [Fact]
    public void GetBestFragment_PhraseTermWithoutAdjacency_Dropped()
    {
        // "search engine" as a phrase, but "search" is alone (no adjacent "engine").
        var termVectors = new List<TermVectorEntry>
        {
            new("search", 1, [1], StartOffsets: [4], EndOffsets: [10])
        };
        var fq = new FieldQuery(new PhraseQuery("body", "search", "engine"));

        string result = _highlighter.GetBestFragment("the search was slow", fq, termVectors);

        // No adjacent "engine" term → "search" should be dropped.
        Assert.DoesNotContain("<b>", result);
        Assert.Equal("the search was slow", result);
    }

    [Fact]
    public void GetBestFragment_MultipleMatchesAtIdenticalOffsets_Deduplicates()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("the", 2, [0, 2], StartOffsets: [0, 8], EndOffsets: [3, 11])
        };
        var fq = new FieldQuery(new TermQuery("body", "the"));

        string result = _highlighter.GetBestFragment("the cat the dog", fq, termVectors);

        Assert.Equal("<b>the</b> cat <b>the</b> dog", result);
    }

    [Fact]
    public void GetBestFragment_SkipsOffsetsOutsideText()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [50], EndOffsets: [55])
        };
        var fq = new FieldQuery(new TermQuery("body", "hello"));

        string result = _highlighter.GetBestFragment("short", fq, termVectors);

        Assert.Equal("short", result);
    }

    [Fact]
    public void GetBestFragment_EmptyFieldQuery_ReturnsTruncatedText()
    {
        var termVectors = new List<TermVectorEntry>
        {
            new("hello", 1, [0], StartOffsets: [0], EndOffsets: [5])
        };
        // A query with no terms (MatchNoDocsQuery has no terms).
        var fq = new FieldQuery(new MatchNoDocsQuery());

        string result = _highlighter.GetBestFragment("hello world", fq, termVectors);

        Assert.Equal("hello world", result);
    }
}
