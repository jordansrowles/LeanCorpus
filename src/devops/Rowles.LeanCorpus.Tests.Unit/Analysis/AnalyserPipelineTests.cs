using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Unit tests for the <see cref="AnalyserPipelineTests"/> class, verifying that the analyser
/// correctly composes a tokeniser with zero or more filters, and that standard components
/// implement their expected interfaces.
/// </summary>
[Trait("Category", "Analysis")]
public sealed class AnalyserPipelineTests
{
    /// <summary>
    /// Ensures that an analyser built with a tokeniser, lowercase filter, and stop-word filter
    /// processes text correctly: lowercases all tokens and removes stop words like "the".
    /// </summary>
    [Fact(DisplayName = "Analyser composes tokeniser and filters correctly")]
    public void Analyser_ComposesTokeniserAndFilters()
    {
        var analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new StopWordFilter());

        var matSink = new MaterialisingTokenSink();
        analyser.Analyse("The Quick Brown Fox Jumps".AsSpan(), matSink);
        var tokens = matSink.Tokens;
        var texts = tokens.Select(t => t.Text).ToList();

        Assert.DoesNotContain("the", texts); // stop word removed
        Assert.Contains("quick", texts);     // lowercased
        Assert.Contains("brown", texts);
        Assert.Contains("fox", texts);
        Assert.Contains("jumps", texts);
    }

    /// <summary>
    /// Verifies that adding a Porter stemmer filter to the pipeline reduces
    /// inflected words (e.g., "Running", "Quickly") to their base forms (e.g., "run").
    /// </summary>
    [Fact(DisplayName = "Analyser with stemmer reduces words to base form")]
    public void Analyser_WithStemmer_StemsTokens()
    {
        var analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new PorterStemmerFilter());

        var matSink = new MaterialisingTokenSink();
        analyser.Analyse("Running Quickly".AsSpan(), matSink);
        var tokens = matSink.Tokens;
        var texts = tokens.Select(t => t.Text).ToList();

        Assert.Contains("run", texts);
    }

    /// <summary>
    /// Confirms that <see cref="StandardAnalyser"/> correctly implements <see cref="IAnalyser"/>
    /// and performs typical analysis: tokenisation + lowercasing.
    /// </summary>
    [Fact(DisplayName = "StandardAnalyser implements IAnalyser and lowercases tokens")]
    public void StandardAnalyser_ImplementsIAnalyser()
    {
        IAnalyser analyser = new StandardAnalyser();
        var matSink = new MaterialisingTokenSink();
        analyser.Analyse("Hello World".AsSpan(), matSink);
        var tokens = matSink.Tokens;
        Assert.Equal(2, tokens.Count);
        Assert.Equal("hello", tokens[0].Text);
        Assert.Equal("world", tokens[1].Text);
    }

    /// <summary>
    /// Tests that <see cref="StopWordFilter"/> correctly implements <see cref="ISpanTokenFilter"/>
    /// by removing common stop words (e.g., "the") from a token list.
    /// </summary>
    [Fact(DisplayName = "StopWordFilter implements ISpanTokenFilter and removes stop words")]
    public void ISpanTokenFilter_StopWordFilter_Implements()
    {
        ISpanTokenFilter filter = new StopWordFilter();
        var tokens = new List<Token> { new("the", 0, 3), new("cat", 4, 7) };
        var sink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, sink);
        var result = sink.Tokens;
        Assert.Single(result);
        Assert.Equal("cat", result[0].Text);
    }
    /// <summary>
    /// Verifies that <see cref="LowercaseFilter"/> implements <see cref="ISpanTokenFilter"/>
    /// and converts token text to lowercase.
    /// </summary>
    [Fact(DisplayName = "LowercaseFilter implements ISpanTokenFilter and lowercases tokens")]
    public void ISpanTokenFilter_LowercaseFilter_Implements()
    {
        ISpanTokenFilter filter = new LowercaseFilter();
        var tokens = new List<Token> { new("HELLO", 0, 5) };
        var sink = new MaterialisingTokenSink();
        foreach (var t in tokens) filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, sink);
        var result = sink.Tokens;
        Assert.Equal("hello", result[0].Text);
    }

    /// <summary>
    /// Ensures that the default <see cref="Tokeniser"/> implements <see cref="ISpanTokeniser"/>
    /// and correctly splits a space‑separated string into tokens.
    /// </summary>
    [Fact(DisplayName = "Tokeniser implements ISpanTokeniser and splits on whitespace")]
    public void ISpanTokeniser_Tokeniser_Implements()
    {
        ISpanTokeniser tokeniser = new Tokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise("one two three".AsSpan(), sink);
        Assert.Equal(3, sink.Tokens.Count);
    }
}
