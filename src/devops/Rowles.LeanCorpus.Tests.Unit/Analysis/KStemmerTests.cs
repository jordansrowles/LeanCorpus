using Rowles.LeanCorpus.Analysis.Stemmers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

[Trait("Category", "Analysis")]
public sealed class KStemmerTests
{
    private static readonly IKStemLexicon Lexicon = KStemLexicon.From(
    [
        "be", "berry", "box", "cat", "child", "church", "city", "create", "cry", "dance",
        "die", "dish", "do", "gas", "generalization", "generalize", "go", "goose", "happy", "have",
        "jump", "kind", "leaf", "lie", "live", "love", "make", "man", "mouse", "move",
        "national", "organize", "peach", "pie", "plan", "quick", "run", "sky", "smile", "stop",
        "tie", "tomato", "use", "woman", "wolf"
    ]);

    private readonly KStemmer _stemmer = new(Lexicon);

    [Theory(DisplayName = "KStemmer: Irregular Forms Return Exceptions")]
    [InlineData("children", "child")]
    [InlineData("women", "woman")]
    [InlineData("men", "man")]
    [InlineData("mice", "mouse")]
    [InlineData("geese", "goose")]
    [InlineData("leaves", "leaf")]
    [InlineData("wolves", "wolf")]
    [InlineData("was", "be")]
    [InlineData("were", "be")]
    [InlineData("been", "be")]
    [InlineData("had", "have")]
    [InlineData("did", "do")]
    [InlineData("went", "go")]
    [InlineData("made", "make")]
    [InlineData("dying", "die")]
    [InlineData("lying", "lie")]
    [InlineData("tying", "tie")]
    [InlineData("skies", "sky")]
    [InlineData("pies", "pie")]
    public void Stem_IrregularForms_ReturnsException(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "KStemmer: Plural Forms Return Base Form")]
    [InlineData("cats", "cat")]
    [InlineData("cities", "city")]
    [InlineData("cries", "cry")]
    [InlineData("berries", "berry")]
    [InlineData("churches", "church")]
    [InlineData("boxes", "box")]
    [InlineData("dishes", "dish")]
    [InlineData("peaches", "peach")]
    [InlineData("tomatoes", "tomato")]
    [InlineData("gases", "gas")]
    public void Stem_PluralForms_ReturnsBaseForm(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "KStemmer: Past Tense Returns Base Form")]
    [InlineData("loved", "love")]
    [InlineData("jumped", "jump")]
    [InlineData("stopped", "stop")]
    [InlineData("planned", "plan")]
    [InlineData("smiled", "smile")]
    [InlineData("created", "create")]
    public void Stem_PastTense_ReturnsBaseForm(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "KStemmer: Progressive Forms Return Base Form")]
    [InlineData("loving", "love")]
    [InlineData("jumping", "jump")]
    [InlineData("running", "run")]
    [InlineData("smiling", "smile")]
    [InlineData("living", "live")]
    [InlineData("moving", "move")]
    [InlineData("dancing", "dance")]
    public void Stem_ProgressiveForms_ReturnsBaseForm(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "KStemmer: Derivational Forms Return Lexicon Base")]
    [InlineData("generalizations", "generalize")]
    [InlineData("nationalize", "national")]
    [InlineData("nationalism", "national")]
    [InlineData("happiness", "happy")]
    [InlineData("kindness", "kind")]
    [InlineData("quickly", "quick")]
    [InlineData("organization", "organize")]
    [InlineData("useful", "use")]
    [InlineData("useless", "use")]
    [InlineData("usefully", "use")]
    public void Stem_DerivationalForms_ReturnsLexiconBase(string input, string expected)
        => Assert.Equal(expected, _stemmer.Stem(input));

    [Theory(DisplayName = "KStemmer: Protected Words Return Self")]
    [InlineData("news")]
    [InlineData("proceed")]
    [InlineData("succeed")]
    [InlineData("exceed")]
    public void Stem_ProtectedWords_ReturnSelf(string word)
        => Assert.Equal(word, _stemmer.Stem(word));

    [Fact(DisplayName = "KStemmer: Null Input Throws")]
    public void Stem_NullInput_Throws()
        => Assert.Throws<ArgumentNullException>(() => _stemmer.Stem(null!));

    [Fact(DisplayName = "KStemmer: Mixed Case Normalises To Lower")]
    public void Stem_MixedCase_NormalisesToLower()
        => Assert.Equal("cat", _stemmer.Stem("CATS"));

    [Fact(DisplayName = "KStemmer: Null Lexicon Throws")]
    public void Ctor_NullLexicon_Throws()
        => Assert.Throws<ArgumentNullException>(() => new KStemmer(null!));

    [Fact(DisplayName = "KStemLexicon: FromFile Loads Lexicon From Disk")]
    public void KStemLexicon_FromFile_LoadsLexicon()
    {
        var lexicon = KStemLexicon.FromFile(ResolveLexiconPath("kstem-dict.txt"));
        Assert.True(lexicon.Contains("abandon"));
    }

    [Fact(DisplayName = "KStemmer: FromFile Lexicon Works End-to-End")]
    public void KStemmer_FromFileLexicon_StemsCorrectly()
    {
        var lexicon = KStemLexicon.FromFile(ResolveLexiconPath("kstem-dict.txt"));
        var stemmer = new KStemmer(lexicon);
        Assert.Equal("tie", stemmer.Stem("tying"));
        Assert.Equal("news", stemmer.Stem("news"));
    }

    private static string ResolveLexiconPath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "lexicons", fileName)))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException($"Could not find lexicons/{fileName}. Ensure the test is run from within the repository.");
        return Path.Combine(dir.FullName, "lexicons", fileName);
    }
}
