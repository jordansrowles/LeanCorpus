using Rowles.LeanCorpus.Analysis.Filters;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>Tests for the optional, content-keyed Hunspell parse cache.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Filters)]
[Collection(HunspellCacheCollection.Name)]
public sealed class HunspellDictionaryCacheTests
{
    private const string Affix = """
        SET UTF-8
        SFX S Y 1
        SFX S 0 s .
        """;

    private static string Dictionary(string word) => $"1\n{word}/S\n";

    private static string UniqueWord() => "w" + Guid.NewGuid().ToString("N")[..12];

    [Fact(DisplayName = "Hunspell cache: null text throws ArgumentNullException")]
    public void Null_text_throws_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => HunspellDictionary.Parse(null!, Dictionary("cat")));
        Assert.Throws<ArgumentNullException>(() => HunspellDictionary.Parse(Affix, null!));
        Assert.Throws<ArgumentNullException>(() => HunspellDictionary.Parse(null!, null!, useCache: false));
    }

    [Fact(DisplayName = "Hunspell cache: identical content shares one cached instance")]
    public void Cached_instances_are_shared()
    {
        string dictionary = Dictionary(UniqueWord());

        Assert.Same(HunspellDictionary.Parse(Affix, dictionary), HunspellDictionary.Parse(Affix, dictionary));
    }

    [Fact(DisplayName = "Hunspell cache: useCache false always returns a new instance")]
    public void UseCache_false_returns_a_new_instance()
    {
        string word = UniqueWord();
        string dictionary = Dictionary(word);
        var cached = HunspellDictionary.Parse(Affix, dictionary);

        var first = HunspellDictionary.Parse(Affix, dictionary, useCache: false);
        var second = HunspellDictionary.Parse(Affix, dictionary, useCache: false);

        Assert.NotSame(cached, first);
        Assert.NotSame(first, second);
        Assert.Equal([word], first.Stem(word + "s"));
        Assert.Same(cached, HunspellDictionary.Parse(Affix, dictionary));
    }

    [Fact(DisplayName = "Hunspell cache: distinct content never shares an entry")]
    public void Distinct_content_never_shares_an_entry()
    {
        string first = UniqueWord();
        string second = UniqueWord();
        var a = HunspellDictionary.Parse(Affix, Dictionary(first));
        var b = HunspellDictionary.Parse(Affix, Dictionary(second));
        var limited = HunspellDictionary.Parse(Affix, Dictionary(first), maxGeneratedFormsPerEntry: 1);

        Assert.NotSame(a, b);
        Assert.NotSame(a, limited);
        Assert.Equal([first], a.Stem(first + "s"));
        Assert.Empty(b.Stem(first + "s"));
    }

    [Fact(DisplayName = "Hunspell cache: ClearCache forces a re-parse")]
    public void ClearCache_forces_a_reparse()
    {
        string dictionary = Dictionary(UniqueWord());
        var before = HunspellDictionary.Parse(Affix, dictionary);

        HunspellDictionary.ClearCache();

        var after = HunspellDictionary.Parse(Affix, dictionary);
        Assert.NotSame(before, after);
        Assert.Same(after, HunspellDictionary.Parse(Affix, dictionary));
    }
}

/// <summary>Serialises tests that clear the process-wide Hunspell cache.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HunspellCacheCollection
{
    public const string Name = "Hunspell cache";
}
