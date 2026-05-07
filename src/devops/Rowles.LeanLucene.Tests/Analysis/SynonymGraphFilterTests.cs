using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Analysis.Filters;

namespace Rowles.LeanLucene.Tests.Analysis;

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

        filter.Apply(tokens);

        Assert.Equal(4, tokens.Count); // quick + fast + rapid + fox
        Assert.Equal("quick", tokens[0].Text);
        Assert.Equal("fast", tokens[1].Text);
        Assert.Equal("rapid", tokens[2].Text);
        Assert.Equal("fox", tokens[3].Text);

        // Synonym tokens share the same offset as source
        Assert.Equal(0, tokens[1].StartOffset);
        Assert.Equal(5, tokens[1].EndOffset);
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

        filter.Apply(tokens);

        // Should match "new york city" (3 tokens) → keep originals + add synonyms
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("nyc", texts);
        Assert.Contains("big apple", texts);
        Assert.Contains("park", texts);
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

        filter.Apply(tokens);

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

        filter.Apply(tokens);

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

        filter.Apply(tokens);

        Assert.Equal(2, tokens.Count);
        Assert.Equal("quick", tokens[0].Text);
        Assert.Equal("fast", tokens[1].Text);
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

        filter.Apply(tokens);

        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("large", texts);
        Assert.Contains("feline", texts);
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

        filter.Apply(tokens);

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

        filter.Apply(tokens);

        // Original "usa" should still be present
        Assert.Equal("usa", tokens[0].Text);
        Assert.Equal(3, tokens.Count); // usa + united states + america
    }
}
