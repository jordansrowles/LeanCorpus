namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Stemmers;

/// <summary>
/// Unit tests for <see cref="IKStemLexicon"/> interface contract.
/// </summary>
[Trait("Category", "Analysis")]
[Trait("Category", "UnitTest")]
public sealed class IKStemLexiconTests
{
    private sealed class TestLexicon : IKStemLexicon
    {
        private readonly HashSet<string> _words = new(StringComparer.Ordinal);
        public TestLexicon(params string[] words) { foreach (var w in words) _words.Add(w); }
        public bool Contains(string word) => _words.Contains(word);
        public bool Contains(ReadOnlySpan<char> word) => _words.Contains(word.ToString());
        public bool ContainsPreLowered(ReadOnlySpan<char> word) => _words.Contains(word.ToString());
    }

    [Fact(DisplayName = "IKStemLexicon: Contains returns true for known word")]
    public void Contains_KnownWord_ReturnsTrue()
    {
        var lexicon = new TestLexicon("house", "dog");
        Assert.True(lexicon.Contains("house"));
    }

    [Fact(DisplayName = "IKStemLexicon: Contains returns false for unknown word")]
    public void Contains_UnknownWord_ReturnsFalse()
    {
        var lexicon = new TestLexicon("house");
        Assert.False(lexicon.Contains("cat"));
    }

    [Fact(DisplayName = "IKStemLexicon: Contains with ReadOnlySpan returns true")]
    public void ContainsSpan_KnownWord_ReturnsTrue()
    {
        var lexicon = new TestLexicon("running");
        Assert.True(lexicon.Contains("running".AsSpan()));
    }

    [Fact(DisplayName = "IKStemLexicon: Contains with ReadOnlySpan returns false")]
    public void ContainsSpan_UnknownWord_ReturnsFalse()
    {
        var lexicon = new TestLexicon("walking");
        Assert.False(lexicon.Contains("jogging".AsSpan()));
    }

    [Fact(DisplayName = "IKStemLexicon: ContainsPreLowered default delegates to Contains")]
    public void ContainsPreLowered_DefaultDelegatesToContainsSpan()
    {
        var lexicon = new TestLexicon("word");
        Assert.True(lexicon.ContainsPreLowered("word".AsSpan()));
        Assert.False(lexicon.ContainsPreLowered("WORD".AsSpan()));
    }

    [Fact(DisplayName = "IKStemLexicon: Empty lexicon contains nothing")]
    public void EmptyLexicon_ContainsNothing()
    {
        var lexicon = new TestLexicon();
        Assert.False(lexicon.Contains("anything"));
        Assert.False(lexicon.Contains("".AsSpan()));
    }

    [Fact(DisplayName = "IKStemLexicon: Multiple words all containable")]
    public void MultipleWords_AllContainable()
    {
        var lexicon = new TestLexicon("alpha", "beta", "gamma");
        Assert.True(lexicon.Contains("alpha"));
        Assert.True(lexicon.Contains("beta".AsSpan()));
        Assert.True(lexicon.ContainsPreLowered("gamma".AsSpan()));
    }
}
