using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for Synonym Graph Filter.
/// </summary>
public class SynonymGraphFilterTests
{
    /// <summary>
    /// Verifies the Single Token Synonym: Expands Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Single Token Synonym: Expands Correctly")]
    public void SingleTokenSynonym_ExpandsCorrectly()
    {
        var map = new SynonymMap();
        map.Add("quick", ["fast", "rapid"]);

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>
        {
            new("quick", 0, 5),
            new("fox", 6, 9)
        };

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        Assert.Equal(2, tokens.Count);
        Assert.Equal("quick", tokens[0].Text);
        Assert.Equal("fox", tokens[1].Text);
    }

    /// <summary>
    /// Verifies the Multi Token Synonym: Longest Match scenario.
    /// </summary>
    [Fact(DisplayName = "Multi Token Synonym: Longest Match")]
    public void MultiTokenSynonym_LongestMatch()
    {
        var map = new SynonymMap();
        map.Add("new york", ["nyc"]);
        map.Add("new york city", ["nyc", "big apple"]);

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>
        {
            new("new", 0, 3),
            new("york", 4, 8),
            new("city", 9, 13),
            new("park", 14, 18)
        };

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        // Should match "new york city" (3 tokens) → keep originals + add synonyms
        Assert.Equal(["new", "york", "city", "park"], tokens.Select(t => t.Text));
    }

    /// <summary>
    /// Verifies the No Match: Passes Through scenario.
    /// </summary>
    [Fact(DisplayName = "No Match: Passes Through")]
    public void NoMatch_PassesThrough()
    {
        var map = new SynonymMap();
        map.Add("quick", ["fast"]);

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>
        {
            new("slow", 0, 4),
            new("fox", 5, 8)
        };

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        Assert.Equal(2, tokens.Count);
        Assert.Equal("slow", tokens[0].Text);
        Assert.Equal("fox", tokens[1].Text);
    }

    /// <summary>
    /// Verifies the Empty Token List: No Error scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Token List: No Error")]
    public void EmptyTokenList_NoError()
    {
        var map = new SynonymMap();
        map.Add("test", ["exam"]);

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>();

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the Case Insensitive: Matches Lowercase scenario.
    /// </summary>
    [Fact(DisplayName = "Case Insensitive: Matches Lowercase")]
    public void CaseInsensitive_MatchesLowercase()
    {
        var map = new SynonymMap();
        map.Add("Quick", ["fast"]); // Added with mixed case

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>
        {
            new("quick", 0, 5) // lowercase in token stream
        };

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        Assert.Single(tokens);
        Assert.Equal("quick", tokens[0].Text);
    }

    /// <summary>
    /// Verifies the Multiple Synonyms In Sequence scenario.
    /// </summary>
    [Fact(DisplayName = "Multiple Synonyms In Sequence")]
    public void MultipleSynonymsInSequence()
    {
        var map = new SynonymMap();
        map.Add("big", ["large"]);
        map.Add("cat", ["feline"]);

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>
        {
            new("big", 0, 3),
            new("cat", 4, 7)
        };

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        Assert.Equal(["big", "cat"], tokens.Select(t => t.Text));
    }

    /// <summary>
    /// Verifies the Synonym Map: Trie Structure Partial Match Not Expanded scenario.
    /// </summary>
    [Fact(DisplayName = "Synonym Map: Trie Structure Partial Match Not Expanded")]
    public void SynonymMap_TrieStructure_PartialMatchNotExpanded()
    {
        var map = new SynonymMap();
        map.Add("ice cream", ["gelato"]);
        // "ice" alone should NOT match

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>
        {
            new("ice", 0, 3),
            new("cold", 4, 8) // not "cream", so no match
        };

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        Assert.Equal(2, tokens.Count);
        Assert.Equal("ice", tokens[0].Text);
        Assert.Equal("cold", tokens[1].Text);
    }

    /// <summary>
    /// Verifies the Original Tokens Preserved: With Synonyms scenario.
    /// </summary>
    [Fact(DisplayName = "Original Tokens Preserved: With Synonyms")]
    public void OriginalTokensPreserved_WithSynonyms()
    {
        var map = new SynonymMap();
        map.Add("usa", ["united states", "america"]);

        var filter = new SynonymGraphFilter(map);
        var tokens = new List<Token>
        {
            new("usa", 0, 3)
        };

        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        // Original "usa" should still be present
        Assert.Equal("usa", tokens[0].Text);
        Assert.Single(tokens);
    }

    /// <summary>
    /// Verifies the Larger Unique Synonym Maps: Expand More Tokens scenario.
    /// </summary>
    [Fact(DisplayName = "Larger Unique Synonym Maps: Expand More Tokens")]
    public void LargerUniqueSynonymMaps_ExpandMoreTokens()
    {
        var baseTokens = new List<Token>
        {
            new("government", 0, 10),
            new("market", 11, 17),
            new("company", 18, 25),
            new("nation", 26, 32),
            new("policy", 33, 39)
        };

        var smallMap = new SynonymMap();
        smallMap.Add("government", ["state"]);
        smallMap.Add("market", ["exchange"]);

        var largeMap = new SynonymMap();
        largeMap.Add("government", ["state"]);
        largeMap.Add("market", ["exchange"]);
        largeMap.Add("company", ["firm"]);
        largeMap.Add("nation", ["country"]);
        largeMap.Add("policy", ["programme"]);

        var smallTokens = new List<Token>(baseTokens);
        var largeTokens = new List<Token>(baseTokens);

        var matSink = new MaterialisingTokenSink();
        foreach (var t in smallTokens) new SynonymGraphFilter(smallMap).Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        smallTokens.Clear();
        smallTokens.AddRange(matSink.Tokens);
        var matSink2 = new MaterialisingTokenSink();
        foreach (var t in largeTokens) new SynonymGraphFilter(largeMap).Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink2);
        largeTokens.Clear();
        largeTokens.AddRange(matSink2.Tokens);

        Assert.Equal(5, smallTokens.Count);
        Assert.Equal(5, largeTokens.Count);
    }
}
