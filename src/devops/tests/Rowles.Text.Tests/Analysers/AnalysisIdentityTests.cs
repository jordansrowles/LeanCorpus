using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// ADR051: analysers declare the identity of their output, which an index records and enforces. An
/// identity is declared only where the output is fully determined by it.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
public sealed class AnalysisIdentityTests
{
    public static TheoryData<string> Languages() => new(AnalyserFactory.SupportedLanguages);

    [Theory]
    [MemberData(nameof(Languages))]
    public void FactoryPipelines_DeclareTheirDefaultIdentity_IncludingThreadLocalCopies(string language)
    {
        var analyser = AnalyserFactory.Create(language);
        string expected = AnalyserFactory.GetDefaultAnalysisId(language);

        Assert.Equal(expected, AnalysisIdentity.Of(analyser));
        Assert.Equal(expected, AnalysisIdentity.Of(((IThreadLocalAnalyser)analyser).CreateThreadLocalAnalyser()));
        Assert.IsType<LanguageAnalyser>(analyser);
    }

    [Fact]
    public void CustomisedPipelines_DeclareNoIdentity()
    {
        Assert.Null(AnalysisIdentity.Of(AnalyserFactory.Create("de", stemmer: null)));
        Assert.Null(AnalysisIdentity.Of(AnalyserFactory.Create("de", new Rowles.LeanCorpus.Analysis.Stemmers.GermanStemmer())));
        Assert.Null(AnalysisIdentity.Of(new LanguageAnalyser(new Tokeniser(), StopWords.German, new Rowles.LeanCorpus.Analysis.Stemmers.GermanStemmer())));
        Assert.Null(AnalysisIdentity.Of(new StandardAnalyser(stopWords: ["the"])));
        Assert.Null(AnalysisIdentity.Of(new StemmedAnalyser(new KeywordMarkerFilter(["running"]))));
        Assert.Null(AnalysisIdentity.Of(new WhitespaceAnalyser()));
    }

    [Fact]
    public void StandardAndStemmedAnalysers_DeclareIdentitiesInTheirDefaultConfiguration()
    {
        string? standard = AnalysisIdentity.Of(new StandardAnalyser());
        string? stemmed = AnalysisIdentity.Of(new StemmedAnalyser());

        Assert.StartsWith("rowles-analysis/standard/", standard, StringComparison.Ordinal);
        Assert.StartsWith("rowles-analysis/stemmed-en/", stemmed, StringComparison.Ordinal);
        Assert.Equal(standard, AnalysisIdentity.Of(new StandardAnalyser(internCacheSize: 16)));
        Assert.Equal(stemmed, AnalysisIdentity.Of(new StemmedAnalyser(keywordMarker: null)));
        Assert.Equal(standard, AnalysisIdentity.Of(new StandardAnalyser().CreateThreadLocalAnalyser()));
        Assert.Equal(stemmed, AnalysisIdentity.Of(new StemmedAnalyser().CreateThreadLocalAnalyser()));
        Assert.NotEqual(standard, stemmed);
    }

    [Fact]
    public void Assign_IdentifiesAnAnalyser_WithoutChangingItsOutput()
    {
        var inner = AnalyserFactory.Create("de", stemmer: null);
        var identified = AnalysisIdentity.Assign(inner, "test/de-exact/1");

        Assert.Equal("test/de-exact/1", AnalysisIdentity.Of(identified));
        Assert.Equal(Terms(inner, "Tomaten und Zwiebeln"), Terms(identified, "Tomaten und Zwiebeln"));
        var copy = ((IThreadLocalAnalyser)identified).CreateThreadLocalAnalyser();
        Assert.Equal("test/de-exact/1", AnalysisIdentity.Of(copy));
        Assert.Equal(Terms(inner, "Tomaten"), Terms(copy, "Tomaten"));

        // Reassigning replaces the identity.
        Assert.Equal("test/de-exact/2", AnalysisIdentity.Of(AnalysisIdentity.Assign(identified, "test/de-exact/2")));
    }

    [Fact]
    public void Assign_ForwardsTermNormalisation_OnlyWhenTheAnalyserSupportsIt()
    {
        var standard = AnalysisIdentity.Assign(new StandardAnalyser(), "test/standard/1");
        var normaliser = Assert.IsAssignableFrom<ITermNormaliser>(standard);
        Assert.True(normaliser.TryNormalise("Tomaten", out var normalised));
        Assert.Equal("tomaten", normalised);

        Assert.False(AnalysisIdentity.Assign(new WhitespaceAnalyser(), "test/whitespace/1") is ITermNormaliser);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unidentified")]
    [InlineData("has space")]
    [InlineData("tab\tseparated")]
    [InlineData("line\nbreak")]
    public void Assign_RejectsInvalidIdentities(string identity)
        => Assert.Throws<ArgumentException>(() => AnalysisIdentity.Assign(new StandardAnalyser(), identity));

    [Fact]
    public void Assign_RejectsOverlongAndNullArguments()
    {
        Assert.Throws<ArgumentException>(() => AnalysisIdentity.Assign(new StandardAnalyser(), new string('x', AnalysisIdentity.MaxLength + 1)));
        Assert.Equal(AnalysisIdentity.MaxLength, AnalysisIdentity.Of(AnalysisIdentity.Assign(new StandardAnalyser(), new string('x', AnalysisIdentity.MaxLength)))!.Length);
        Assert.Throws<ArgumentNullException>(() => AnalysisIdentity.Assign(new StandardAnalyser(), null!));
        Assert.Throws<ArgumentNullException>(() => AnalysisIdentity.Assign(null!, "test/x/1"));
        Assert.Throws<ArgumentNullException>(() => AnalysisIdentity.Of(null!));
    }

    [Fact]
    public void Assign_ThreadLocalCopy_RequiresAnOwnershipContract()
    {
        var identified = (IThreadLocalAnalyser)AnalysisIdentity.Assign(new UnownedAnalyser(), "test/unowned/1");
        var exception = Assert.Throws<InvalidOperationException>(() => identified.CreateThreadLocalAnalyser());
        Assert.Contains(nameof(UnownedAnalyser), exception.Message, StringComparison.Ordinal);
    }

    private static string[] Terms(IAnalyser analyser, string text)
    {
        var sink = new MaterialisingTokenSink();
        analyser.Analyse(text, sink);
        return sink.Tokens.Select(static token => token.Text).ToArray();
    }

    private sealed class UnownedAnalyser : IAnalyser
    {
        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink) => sink.Add(input, 0, input.Length);
    }
}
