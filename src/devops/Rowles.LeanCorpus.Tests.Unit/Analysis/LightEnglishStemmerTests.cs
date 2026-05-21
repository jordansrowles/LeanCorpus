using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

[Trait("Category", "Analysis")]
public sealed class LightEnglishStemmerTests
{
    private readonly LightEnglishStemmer _stemmer = new();

    [Theory(DisplayName = "LightEnglish: Irregular Forms Return Exceptions")]
    [InlineData("skies", "sky")]
    [InlineData("dying", "die")]
    [InlineData("lying", "lie")]
    [InlineData("tying", "tie")]
    public void Stem_IrregularForms_ReturnsException(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Plural Forms Return Base")]
    [InlineData("cats", "cat")]
    [InlineData("cities", "city")]
    [InlineData("cries", "cry")]
    [InlineData("berries", "berry")]
    [InlineData("churches", "church")]
    [InlineData("boxes", "box")]
    [InlineData("dishes", "dish")]
    [InlineData("glasses", "glass")]
    [InlineData("buses", "bus")]
    public void Stem_PluralForms_ReturnsBase(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Words Ending In 's' Kept If Not Plural")]
    [InlineData("news")]
    [InlineData("innings")]
    [InlineData("status")]
    public void Stem_NonPluralS_ReturnsSelf(string word)
        => Assert.Equal(word, _stemmer.Stem(word));

    [Theory(DisplayName = "LightEnglish: Past Tense Returns Base")]
    [InlineData("jumped", "jump")]
    [InlineData("stopped", "stop")]
    [InlineData("planned", "plan")]
    public void Stem_PastTense_ReturnsBase(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Past Tense Drops ed But Keeps Stem")]
    [InlineData("loved", "lov")]
    [InlineData("smiled", "smil")]
    [InlineData("created", "creat")]
    public void Stem_PastTense_DropsEd(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Past Tense No Vowel Returns Self")]
    [InlineData("psst")]
    public void Stem_NoVowelPastTense_ReturnsSelf(string word)
        => Assert.Equal(word, _stemmer.Stem(word));

    [Theory(DisplayName = "LightEnglish: Progressive Forms Drop ing")]
    [InlineData("jumping", "jump")]
    [InlineData("running", "run")]
    public void Stem_ProgressiveForms_DropsIng(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Progressive With Vowel Stem Keeps Stem")]
    [InlineData("loving", "lov")]
    [InlineData("smiling", "smil")]
    [InlineData("living", "liv")]
    [InlineData("moving", "mov")]
    [InlineData("dancing", "danc")]
    public void Stem_ProgressiveForms_KeepsStem(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Derivational Suffixes Are Removed")]
    [InlineData("kindness", "kind")]
    [InlineData("movement", "move")]
    [InlineData("slowly", "slow")]
    public void Stem_DerivationalForms_ReturnsBase(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Derivational Keeps Stem Without I-to-Y")]
    [InlineData("happiness", "happi")]
    [InlineData("quickly", "quick")]
    public void Stem_Derivational_KeepsStem(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "LightEnglish: Protected Words Return Self")]
    [InlineData("news")]
    [InlineData("innings")]
    [InlineData("proceed")]
    [InlineData("exceed")]
    [InlineData("succeed")]
    public void Stem_ProtectedWords_ReturnSelf(string word)
        => Assert.Equal(word, _stemmer.Stem(word));

    [Fact(DisplayName = "LightEnglish: Short Words Return Self")]
    public void Stem_ShortWords_ReturnSelf()
    {
        Assert.Equal("in", _stemmer.Stem("in"));
        Assert.Equal("at", _stemmer.Stem("at"));
        Assert.Equal("be", _stemmer.Stem("be"));
    }

    [Fact(DisplayName = "LightEnglish: Mixed Case Normalises To Lower")]
    public void Stem_MixedCase_NormalisesToLower()
        => Assert.Equal("cat", _stemmer.Stem("CATS"));

    [Fact(DisplayName = "LightEnglish: Words Ending With 'ss' Not Stripped")]
    public void Stem_SsEnding_NotStripped()
    {
        Assert.Equal("pass", _stemmer.Stem("pass"));
        Assert.Equal("kiss", _stemmer.Stem("kiss"));
    }

    [Fact(DisplayName = "LightEnglish: Words Ending With 'us' Not Stripped")]
    public void Stem_UsEnding_NotStripped()
    {
        Assert.Equal("bonus", _stemmer.Stem("bonus"));
        Assert.Equal("virus", _stemmer.Stem("virus"));
    }

    [Fact(DisplayName = "LightEnglish: Progressive With E-Restoration For at/bl/iz")]
    public void Stem_Progressive_ERestoration()
    {
        Assert.Equal("create", _stemmer.Stem("creating"));
        Assert.Equal("recognize", _stemmer.Stem("recognizing"));
    }
}
