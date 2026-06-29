namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Stemmers;

/// <summary>
/// Unit tests for <see cref="RussianStemmer"/>.
/// </summary>
[Trait("Category", "Analysis")]
[Trait("Category", "UnitTest")]
public sealed class RussianStemmerTests
{
    private readonly RussianStemmer _stemmer = new();

    [Fact(DisplayName = "RussianStemmer: Stem(string) returns string for short word")]
    public void StemString_ShortWord_ReturnsSame()
    {
        Assert.Equal("да", _stemmer.Stem("да"));
        Assert.Equal("он", _stemmer.Stem("он"));
    }

    [Fact(DisplayName = "RussianStemmer: Stem(Span) returns length for short word")]
    public void StemSpan_ShortWord_ReturnsLength()
    {
        Span<char> buf = stackalloc char[10];
        int len = _stemmer.Stem("ты".AsSpan(), buf);
        Assert.Equal(2, len);
        Assert.Equal("ты", buf[..len].ToString());
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips noun ending ость")]
    public void StemString_StripsOst()
    {
        var result = _stemmer.Stem("радость");
        Assert.Equal("рад", result);
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips adjective ending ый")]
    public void StemString_StripsAdjectiveEnding()
    {
        var result = _stemmer.Stem("красный");
        Assert.Equal("красн", result);
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips adjective ending ая")]
    public void StemString_StripsAya()
    {
        var result = _stemmer.Stem("большая");
        Assert.Equal("больш", result);
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips verb ending ать")]
    public void StemString_StripsAt()
    {
        var result = _stemmer.Stem("читать");
        Assert.Equal("чит", result);
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips reflexive ся")]
    public void StemString_StripsReflexive()
    {
        var result = _stemmer.Stem("находиться");
        Assert.Equal("наход", result);
    }

    [Fact(DisplayName = "RussianStemmer: Stem single character returns same")]
    public void StemSpan_SingleChar_ReturnsLength()
    {
        Span<char> buf = stackalloc char[5];
        int len = _stemmer.Stem("я".AsSpan(), buf);
        Assert.Equal(1, len);
        Assert.Equal("я", buf[..len].ToString());
    }

    [Fact(DisplayName = "RussianStemmer: Stem two characters returns same")]
    public void StemString_TwoChars_ReturnsSame()
    {
        Assert.Equal("мы", _stemmer.Stem("мы"));
    }

    [Fact(DisplayName = "RussianStemmer: Stem buffer too small returns -1")]
    public void StemSpan_BufferTooSmall_ReturnsMinusOne()
    {
        Span<char> buf = stackalloc char[2];
        int len = _stemmer.Stem("длинноеслово".AsSpan(), buf);
        Assert.Equal(-1, len);
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips ий ending")]
    public void StemString_StripsIy()
    {
        var result = _stemmer.Stem("синий");
        Assert.Equal("син", result);
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips plural ы")]
    public void StemString_StripsY()
    {
        var result = _stemmer.Stem("столы");
        Assert.Equal("стол", result);
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) empty returns empty")]
    public void StemString_Empty_ReturnsEmpty()
    {
        Assert.Equal("", _stemmer.Stem(""));
    }

    [Fact(DisplayName = "RussianStemmer: Stem buffer exact size works")]
    public void StemSpan_ExactSize_Works()
    {
        Span<char> buf = stackalloc char[10];
        int len = _stemmer.Stem("радость".AsSpan(), buf);
        Assert.Equal(3, len);
        Assert.Equal("рад", buf[..len].ToString());
    }

    [Fact(DisplayName = "RussianStemmer: Stem(string) strips ость and ь")]
    public void StemString_StripsOstAndSoftSign()
    {
        var result = _stemmer.Stem("новость");
        Assert.Equal("нов", result);
    }
}
