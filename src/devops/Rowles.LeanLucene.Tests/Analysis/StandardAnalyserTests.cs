using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Standard Analyser.
/// </summary>
[Trait("Category", "Analysis")]
public class StandardAnalyserTests
{
    private readonly StandardAnalyser _analyser = new();

    /// <summary>
    /// Verifies the Analyse: All Stop Words Returns Only Non Stop Words scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: All Stop Words Returns Only Non Stop Words")]
    public void Analyse_AllStopWords_ReturnsOnlyNonStopWords()
    {
        // "to", "be", "or", "not" are all stop words in the extended list; only "live" survives.
        var result = _analyser.Analyse("to be or not to live");

        Assert.Single(result);
        Assert.Equal("live", result[0].Text);
    }

    /// <summary>
    /// Verifies the Analyse: Mixed Case With Stop Words Returns Lowercased Non Stop Words scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: Mixed Case With Stop Words Returns Lowercased Non Stop Words")]
    public void Analyse_MixedCaseWithStopWords_ReturnsLowercasedNonStopWords()
    {
        var result = _analyser.Analyse("Running quickly in THE forest");

        Assert.Equal(3, result.Count);
        Assert.Equal("running", result[0].Text);
        Assert.Equal("quickly", result[1].Text);
        Assert.Equal("forest", result[2].Text);
    }

    /// <summary>
    /// Verifies the Analyse: After Returns Token scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: After Returns Token")]
    public void Analyse_After_ReturnsToken()
    {
        var result = _analyser.Analyse("after");

        Assert.Single(result);
        Assert.Equal("after", result[0].Text);
    }

    /// <summary>
    /// Verifies the Analyse: Empty Input Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: Empty Input Returns Empty List")]
    public void Analyse_EmptyInput_ReturnsEmptyList()
    {
        var result = _analyser.Analyse(ReadOnlySpan<char>.Empty);

        Assert.Empty(result);
    }

    /// <summary>
    /// Verifies the Analyse: Single Non Stop Word Returns Single Lowercased Token scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: Single Non Stop Word Returns Single Lowercased Token")]
    public void Analyse_SingleNonStopWord_ReturnsSingleLowercasedToken()
    {
        var result = _analyser.Analyse("Hello");

        Assert.Single(result);
        Assert.Equal("hello", result[0].Text);
    }
}
