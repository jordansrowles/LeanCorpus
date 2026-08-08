using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for Lowercase Filter.
/// </summary>
[Trait("Category", "Analysis")]
public class LowercaseFilterTests
{
    private readonly LowercaseFilter _filter = new();

    /// <summary>
    /// Verifies the Apply: Mixed Case Input Lowercases In Place scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Mixed Case Input Lowercases In Place")]
    public void Apply_MixedCaseInput_LowercasesInPlace()
    {
        var buffer = "Hello WORLD FoO".ToCharArray();

        _filter.Apply(buffer);

        Assert.Equal("hello world foo", new string(buffer));
    }

    /// <summary>
    /// Verifies the Apply: Already Lowercase Remains Unchanged scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Already Lowercase Remains Unchanged")]
    public void Apply_AlreadyLowercase_RemainsUnchanged()
    {
        var buffer = "abc".ToCharArray();

        _filter.Apply(buffer);

        Assert.Equal("abc", new string(buffer));
    }

    /// <summary>
    /// Verifies the Apply: Empty Buffer Does Not Throw scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Empty Buffer Does Not Throw")]
    public void Apply_EmptyBuffer_DoesNotThrow()
    {
        _filter.Apply(Span<char>.Empty);
    }

    /// <summary>
    /// Verifies that non-ASCII uppercase characters (diacritic capitals like Č, Š, Ž)
    /// are lowercased in-place via <see cref="LowercaseFilter.Apply(Span{char})"/>.
    /// </summary>
    [Fact(DisplayName = "Apply: Diacritic uppercase lowercased in place")]
    public void Apply_DiacriticUppercase_LowercasesInPlace()
    {
        var buffer = "ČISTÝ".ToCharArray();

        _filter.Apply(buffer);

        Assert.Equal("čistý", new string(buffer));
    }

    /// <summary>
    /// Verifies that the <see cref="ISpanTokenFilter"/> path handles a non-ASCII uppercase
    /// character before ASCII uppercase characters in the token.
    /// </summary>
    [Fact(DisplayName = "Apply: Token path lowercases diacritic prefix")]
    public void ApplyToken_DiacriticPrefix_Lowercases()
    {
        var analyser = new Analyser(new LetterTokeniser(), _filter);
        var sink = new MaterialisingTokenSink();

        analyser.Analyse("ŠKOLA", sink);

        Assert.Equal("škola", sink.Tokens[0].Text);
    }

    /// <summary>
    /// Verifies that mixed ASCII and non-ASCII uppercase are both lowercased.
    /// </summary>
    [Fact(DisplayName = "Apply: Mixed ASCII and diacritic uppercase")]
    public void Apply_MixedAsciiAndDiacritic_LowercasesBoth()
    {
        var buffer = "ŽIADNY HELLO".ToCharArray();

        _filter.Apply(buffer);

        Assert.Equal("žiadny hello", new string(buffer));
    }

    /// <summary>
    /// Verifies that already-lowercase diacritic text is unchanged.
    /// </summary>
    [Fact(DisplayName = "Apply: Lowercase diacritic text unchanged")]
    public void Apply_LowercaseDiacritic_Unchanged()
    {
        var buffer = "čistý škola".ToCharArray();

        _filter.Apply(buffer);

        Assert.Equal("čistý škola", new string(buffer));
    }
}
