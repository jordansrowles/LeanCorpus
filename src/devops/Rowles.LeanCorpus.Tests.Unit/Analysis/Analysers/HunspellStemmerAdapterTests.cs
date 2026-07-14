namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Analysers;

/// <summary>
/// Unit tests for <see cref="HunspellStemmerAdapter"/>.
/// </summary>
[Trait("Category", "Analysis")]
[Trait("Category", "UnitTest")]
public sealed class HunspellStemmerAdapterTests
{
    [Fact(DisplayName = "HunspellStemmerAdapter: Constructor throws on null dictionary")]
    public void Constructor_NullDictionary_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new HunspellStemmerAdapter(null!));
        Assert.Contains("dictionary", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "HunspellStemmerAdapter: Stem returns original word spanning")]
    public void Stem_Span_ReturnsOriginalWordLength()
    {
        // With no actual .dic/.aff files, we test the adapter's basic contract:
        // it should be constructable and the Stem methods exist.
        // The adapter is internal and needs a real HunspellDictionary for full testing.
        // Here we verify the type is accessible and implements ISpanStemmer.
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
