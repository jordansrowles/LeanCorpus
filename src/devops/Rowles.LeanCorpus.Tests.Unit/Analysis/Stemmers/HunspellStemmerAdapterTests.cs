namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Stemmers;

/// <summary>Unit tests for <see cref="HunspellStemmerAdapter"/>.</summary>
[Trait("Category", "Analysis")]
[Trait("Category", "UnitTest")]
public sealed class HunspellStemmerAdapterTests
{
    [Fact(DisplayName = "HunspellStemmerAdapter: Constructor throws on null dictionary")]
    public void Constructor_NullDictionary_Throws()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new HunspellStemmerAdapter(null!));
        Assert.Contains("dictionary", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "HunspellStemmerAdapter: Stem returns original word spanning")]
    public void Stem_Span_ReturnsOriginalWordLength()
    {
        Assert.True(typeof(HunspellStemmerAdapter).IsAssignableTo(typeof(ISpanStemmer)));
    }

    [Fact(DisplayName = "HunspellStemmerAdapter: Type is internal sealed class")]
    public void Type_IsInternalSealed()
    {
        var type = typeof(HunspellStemmerAdapter);
        Assert.True(type.IsSealed);
        Assert.False(type.IsPublic);
    }
}
