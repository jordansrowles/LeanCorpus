using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

[Trait("Category", "Analysis")]
public sealed class AdvancedFilterTests
{
    [Fact(DisplayName = "Keep Word Filter: Retains Only Configured Words")]
    public void KeepWordFilter_RetainsOnlyConfiguredWords()
    {
        var tokens = new List<Token> { new("search", 0, 6), new("engine", 7, 13), new("corpus", 14, 20) };
        var filter = new KeepWordFilter(["search", "corpus"]);

        filter.Apply(tokens);

        Assert.Equal(["search", "corpus"], tokens.Select(static token => token.Text));
    }

    [Fact(DisplayName = "Limit Token Count Filter: Truncates Stream")]
    public void LimitTokenCountFilter_TruncatesStream()
    {
        var tokens = new List<Token> { new("a", 0, 1), new("b", 2, 3), new("c", 4, 5) };
        var filter = new LimitTokenCountFilter(2);

        filter.Apply(tokens);

        Assert.Equal(["a", "b"], tokens.Select(static token => token.Text));
    }

    [Fact(DisplayName = "Type Token Filter: Keeps Matching Types")]
    public void TypeTokenFilter_KeepsMatchingTypes()
    {
        var tokens = new List<Token>
        {
            new("search", 0, 6, MediaWikiTokeniser.CategoryType),
            new("example.com", 7, 18, Uax29UrlEmailTokeniser.UrlType),
            new("engine", 19, 25, MediaWikiTokeniser.InternalLinkType)
        };
        var filter = new TypeTokenFilter([MediaWikiTokeniser.CategoryType, MediaWikiTokeniser.InternalLinkType]);

        filter.Apply(tokens);

        Assert.Equal([MediaWikiTokeniser.CategoryType, MediaWikiTokeniser.InternalLinkType], tokens.Select(static token => token.Type));
    }

    [Fact(DisplayName = "Metaphone Filter: Injected Code Shares Position")]
    public void MetaphoneFilter_InjectedCode_SharesPosition()
    {
        var tokens = new List<Token> { new("phone", 0, 5) };
        var filter = new MetaphoneFilter();

        filter.Apply(tokens);

        Assert.Equal(["phone", "FN"], tokens.Select(static token => token.Text));
        Assert.Equal([1, 0], tokens.Select(static token => token.PositionIncrement));
    }

    [Fact(DisplayName = "Phonetic Alternates Filter: Emits Alternates At Same Position")]
    public void PhoneticAlternatesFilter_EmitsAlternatesAtSamePosition()
    {
        var tokens = new List<Token> { new("schwarz", 0, 7) };
        var filter = new PhoneticAlternatesFilter();

        filter.Apply(tokens);

        Assert.True(tokens.Count >= 2);
        Assert.Equal("schwarz", tokens[0].Text);
        Assert.All(tokens.Skip(1), static token => Assert.Equal(0, token.PositionIncrement));
    }

    [Fact(DisplayName = "Hunspell Dictionary: Bracketed Suffix Condition Is Honoured")]
    public void HunspellDictionary_BracketedSuffixCondition_IsHonoured()
    {
        const string aff = """
SET UTF-8
SFX Y Y 1
SFX Y y ies [^aeiou]y
""";
        const string dic = """
2
party/Y
play/Y
""";
        var dictionary = HunspellDictionary.Parse(aff, dic);

        Assert.Contains("party", dictionary.Stem("parties"));
        Assert.Empty(dictionary.Stem("plaies"));
    }

    [Fact(DisplayName = "Hunspell Dictionary: Cross Product Suffix Condition Uses Prefix Candidate")]
    public void HunspellDictionary_CrossProductSuffixCondition_UsesPrefixCandidate()
    {
        const string aff = """
SET UTF-8
PFX R Y 1
PFX R 0 re .
SFX D Y 1
SFX D 0 ing rework
""";
        const string dic = """
1
work/RD
""";

        var dictionary = HunspellDictionary.Parse(aff, dic);

        Assert.Contains("work", dictionary.Stem("reworking"));
    }

    [Fact(DisplayName = "Hunspell Dictionary: Malformed Cross Product Does Not Throw")]
    public void HunspellDictionary_MalformedCrossProduct_DoesNotThrow()
    {
        const string aff = """
SET UTF-8
PFX R Y 1
PFX R abc 0 .
SFX D Y 1
SFX D abc x .
""";
        const string dic = """
1
abc/RD
""";

        var dictionary = HunspellDictionary.Parse(aff, dic);

        Assert.Contains("abc", dictionary.Stem("abc"));
    }

    [Fact(DisplayName = "Hunspell Dictionary: AFF Count Rejects Wrong Directive")]
    public void HunspellDictionary_AffCountRejectsWrongDirective()
    {
        const string aff = """
SET UTF-8
PFX A Y 2
PFX A 0 re .
SFX A 0 ing .
""";
        const string dic = """
1
work/A
""";

        Assert.Throws<InvalidDataException>(() => HunspellDictionary.Parse(aff, dic));
    }

    [Fact(DisplayName = "Hunspell Dictionary: AFF Count Rejects Wrong Flag")]
    public void HunspellDictionary_AffCountRejectsWrongFlag()
    {
        const string aff = """
SET UTF-8
SFX A Y 1
SFX B 0 ing .
""";
        const string dic = """
1
work/A
""";

        Assert.Throws<InvalidDataException>(() => HunspellDictionary.Parse(aff, dic));
    }

    [Fact(DisplayName = "Hunspell Dictionary: Generated Form Limit Rejects Runaway Rules")]
    public void HunspellDictionary_GeneratedFormLimit_RejectsRunawayRules()
    {
        const string aff = """
SET UTF-8
PFX A Y 2
PFX A 0 re .
PFX A 0 pre .
SFX B Y 2
SFX B 0 ing .
SFX B 0 ed .
""";
        const string dic = """
1
play/AB
""";

        Assert.Throws<InvalidDataException>(() => HunspellDictionary.Parse(aff, dic, maxGeneratedFormsPerEntry: 2));
    }

    [Fact(DisplayName = "Flatten Graph Filter: Normalises Leading Zero Position Increment")]
    public void FlattenGraphFilter_NormalisesLeadingZeroPositionIncrement()
    {
        var tokens = new List<Token>
        {
            new("alpha", 0, 5, positionIncrement: 0),
            new("ALF", 0, 5, positionIncrement: 0),
            new("beta", 6, 10, positionIncrement: 1)
        };
        var filter = new FlattenGraphFilter();

        filter.Apply(tokens);

        Assert.Equal([1, 0, 1], tokens.Select(static token => token.PositionIncrement));
    }

    [Fact(DisplayName = "Hunspell Dictionary: Prefix Suffix Cross Product Generates Stem")]
    public void HunspellDictionary_PrefixSuffixCrossProduct_GeneratesStem()
    {
        const string aff = """
SET UTF-8
PFX R Y 1
PFX R 0 re .
SFX D Y 1
SFX D 0 ing .
""";
        const string dic = """
1
play/RD
""";
        var dictionary = HunspellDictionary.Parse(aff, dic);

        Assert.Contains("play", dictionary.Stem("replaying"));
    }

    [Fact(DisplayName = "Hunspell Stem Filter: Replaces Surface Form With Stem")]
    public void HunspellStemFilter_ReplacesSurfaceFormWithStem()
    {
        const string aff = """
SET UTF-8
SFX D Y 1
SFX D 0 ing .
""";
        const string dic = """
1
play/D
""";
        var filter = new HunspellStemFilter(HunspellDictionary.Parse(aff, dic));
        var tokens = new List<Token> { new("playing", 0, 7) };

        filter.Apply(tokens);

        Assert.Equal("play", tokens[0].Text);
    }
}
