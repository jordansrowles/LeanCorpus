using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for Stemmed Analyser.
/// </summary>
[Trait("Category", "Analysis")]
public class StemmedAnalyserTests
{
    private readonly StemmedAnalyser _analyser = new();

    /// <summary>
    /// Verifies the Analyse: Stemmed Words Returns Stemmed Tokens scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: Stemmed Words Returns Stemmed Tokens")]
    public void Analyse_StemmedWords_ReturnsStemmedTokens()
    {
        // Arrange
        var input = "running jumped quickly";

        // Act
        var matSink = new MaterialisingTokenSink();
        _analyser.Analyse(input.AsSpan(), matSink);
        var tokens = matSink.Tokens;

        // Assert — Porter stems: running→run, jumped→jump, quickly→quickli
        Assert.Equal(3, tokens.Count);
        Assert.Equal("run", tokens[0].Text);
        Assert.Equal("jump", tokens[1].Text);
        Assert.Equal("quickli", tokens[2].Text);
    }

    /// <summary>
    /// Verifies the Analyse: Stop Words Removed Before Stemming scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: Stop Words Removed Before Stemming")]
    public void Analyse_StopWordsRemoved_BeforeStemming()
    {
        // Arrange
        var input = "the cats are running";

        // Act
        var matSink = new MaterialisingTokenSink();
        _analyser.Analyse(input.AsSpan(), matSink);
        var tokens = matSink.Tokens;

        // Assert — "the" and "are" removed as stop words, then "cats"→"cat", "running"→"run"
        Assert.Equal(2, tokens.Count);
        Assert.Equal("cat", tokens[0].Text);
        Assert.Equal("run", tokens[1].Text);
    }

    /// <summary>
    /// Verifies the Analyse: Empty Input Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Analyse: Empty Input Returns Empty List")]
    public void Analyse_EmptyInput_ReturnsEmptyList()
    {
        var matSink = new MaterialisingTokenSink();
        _analyser.Analyse(ReadOnlySpan<char>.Empty, matSink);
        var tokens = matSink.Tokens;
        Assert.Empty(tokens);
    }
}
