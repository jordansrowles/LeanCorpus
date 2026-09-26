namespace Rowles.Text.Tests.Filters;

[Category(TestCategory.Unit)]
[Area(TestArea.Filters)]
public sealed class CharFilterOffsetCorrectionTests
{
    [Fact(DisplayName = "Char filter offsets: deletion before token maps to original source")]
    public void DeletionBeforeTokenMapsToOriginalSource()
    {
        const string source = "prefix123 target";
        CharFilterResult result = Apply(source, new PatternReplaceCharFilter(@"\d+", ""));

        Assert.Equal("prefix target", result.Text);
        AssertTargetOffsets(result, source, 10, 16);
    }

    [Fact(DisplayName = "Char filter offsets: insertion before token maps to original source")]
    public void InsertionBeforeTokenMapsToOriginalSource()
    {
        const string source = "target";
        CharFilterResult result = Apply(source, new PatternReplaceCharFilter("^", "added "));

        Assert.Equal("added target", result.Text);
        AssertTargetOffsets(result, source, 0, 6);
    }

    [Fact(DisplayName = "Char filter offsets: HTML tag removal maps token to original source")]
    public void HtmlTagRemovalMapsTokenToOriginalSource()
    {
        const string source = "<b>target</b>";
        CharFilterResult result = Apply(source, new HtmlStripCharFilter());

        Assert.Equal(" target ", result.Text);
        AssertTargetOffsets(result, source, 3, 9);
    }

    [Fact(DisplayName = "Char filter offsets: HTML entity collapse maps token to original source")]
    public void HtmlEntityCollapseMapsTokenToOriginalSource()
    {
        const string source = "&amp;target";
        CharFilterResult result = Apply(source, new HtmlStripCharFilter());

        Assert.Equal(" target", result.Text);
        AssertTargetOffsets(result, source, 5, 11);
    }

    [Fact(DisplayName = "Char filter offsets: shorter regex replacement maps following token")]
    public void ShorterRegexReplacementMapsFollowingToken()
    {
        const string source = "abc target";
        CharFilterResult result = Apply(source, new PatternReplaceCharFilter("^abc", "x"));

        Assert.Equal("x target", result.Text);
        AssertTargetOffsets(result, source, 4, 10);
    }

    [Fact(DisplayName = "Char filter offsets: longer regex replacement maps following token")]
    public void LongerRegexReplacementMapsFollowingToken()
    {
        const string source = "a target";
        CharFilterResult result = Apply(source, new PatternReplaceCharFilter("^a", "replacement"));

        Assert.Equal("replacement target", result.Text);
        AssertTargetOffsets(result, source, 2, 8);
    }

    [Fact(DisplayName = "Char filter offsets: chained length changes map to original source")]
    public void ChainedLengthChangesMapToOriginalSource()
    {
        const string source = "x123 target";
        CharFilterResult result = Apply(
            source,
            new PatternReplaceCharFilter(@"\d+", ""),
            new PatternReplaceCharFilter("^x", "replacement"));

        Assert.Equal("replacement target", result.Text);
        AssertTargetOffsets(result, source, 5, 11);
    }

    [Fact(DisplayName = "Char filter offsets: mapping filter deletion maps to original source")]
    public void MappingFilterDeletionMapsToOriginalSource()
    {
        const string source = "removed target";
        var filter = new MappingCharFilter(new Dictionary<string, string> { ["removed"] = "" });
        CharFilterResult result = Apply(source, filter);

        Assert.Equal(" target", result.Text);
        AssertTargetOffsets(result, source, 8, 14);
    }

    [Fact(DisplayName = "Char filter offsets: source positions count UTF-16 code units")]
    public void SourceOffsetsCountUtf16CodeUnits()
    {
        const string source = "🙂123 target";
        CharFilterResult result = Apply(source, new PatternReplaceCharFilter(@"\d+", ""));

        Assert.Equal("🙂 target", result.Text);
        AssertTargetOffsets(result, source, 6, 12);
    }

    [Fact(DisplayName = "Char filter offsets: unchanged-length mapping uses identity coordinates")]
    public void UnchangedLengthMappingUsesIdentityCoordinates()
    {
        const string source = "\u201C target \u201D";
        var filter = new MappingCharFilter(new Dictionary<string, string>
        {
            ["\u201C"] = "\"",
            ["\u201D"] = "\""
        });
        CharFilterResult result = Apply(source, filter);

        Assert.Equal("\" target \"", result.Text);
        Assert.True(result.OffsetCorrections.IsIdentity);
        AssertTargetOffsets(result, source, 2, 8);
    }

    private static CharFilterResult Apply(string source, params ICharFilter[] filters)
    {
        var result = new CharFilterResult(source);
        foreach (ICharFilter filter in filters)
            result = result.Apply(filter);
        return result;
    }

    private static void AssertTargetOffsets(CharFilterResult result, string source, int startOffset, int endOffset)
    {
        var sink = new MaterialisingTokenSink();
        result.Analyse(new WhitespaceAnalyser(), sink);

        var target = Assert.Single(sink.Tokens, static token => token.Text == "target");
        Assert.Equal(startOffset, target.StartOffset);
        Assert.Equal(endOffset, target.EndOffset);
        Assert.Equal(1, target.PositionIncrement);
        Assert.Equal(1, target.PositionLength);
        Assert.Equal("target", source[target.StartOffset..target.EndOffset]);
    }
}
