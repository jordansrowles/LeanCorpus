using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Unit tests for the <see cref="DecimalDigitFilter"/> class.
/// Verifies that Unicode decimal digits are normalised to ASCII digits.
/// </summary>
[Trait("Category", "Analysis")]
public class DecimalDigitFilterTests
{
    private readonly DecimalDigitFilter _filter = new();

    /// <summary>
    /// Tests that Arabic-Indic digits are normalised to ASCII digits.
    /// </summary>
    [Fact(DisplayName = "Apply: Arabic-Indic digits normalise to ASCII")]
    public void Apply_ArabicIndicDigits_NormalisesToAscii()
    {
        var sink = new MaterialisingTokenSink();
        _filter.Apply("\u0660\u0661\u0662\u0663\u0664\u0665\u0666\u0667\u0668\u0669".AsSpan(), 0, 10, Token.DefaultType, 1, null, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal("0123456789", sink.Tokens[0].Text);
    }

    /// <summary>
    /// Tests that Devanagari digits are normalised to ASCII digits.
    /// </summary>
    [Fact(DisplayName = "Apply: Devanagari digits normalise to ASCII")]
    public void Apply_DevanagariDigits_NormalisesToAscii()
    {
        var sink = new MaterialisingTokenSink();
        _filter.Apply("\u0966\u0967\u0968\u0969\u096A\u096B\u096C\u096D\u096E\u096F".AsSpan(), 0, 10, Token.DefaultType, 1, null, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal("0123456789", sink.Tokens[0].Text);
    }

    /// <summary>
    /// Tests that ASCII digits are left unchanged while adjacent Unicode digits are normalised.
    /// </summary>
    [Fact(DisplayName = "Apply: mixed ASCII and Unicode digits normalise correctly")]
    public void Apply_MixedAsciiAndUnicodeDigits_NormalisesCorrectly()
    {
        var sink = new MaterialisingTokenSink();
        _filter.Apply("12\u0663\u066445".AsSpan(), 0, 6, Token.DefaultType, 1, null, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal("123445", sink.Tokens[0].Text);
    }

    /// <summary>
    /// Tests that tokens containing only ASCII digits are forwarded unchanged.
    /// </summary>
    [Fact(DisplayName = "Apply: ASCII-only digits remain unchanged")]
    public void Apply_AsciiDigits_RemainUnchanged()
    {
        var sink = new MaterialisingTokenSink();
        _filter.Apply("12345".AsSpan(), 0, 5, Token.DefaultType, 1, null, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal("12345", sink.Tokens[0].Text);
    }

    /// <summary>
    /// Tests that Roman numerals and letters are not treated as decimal digits.
    /// </summary>
    [Fact(DisplayName = "Apply: Roman numerals and letters remain unchanged")]
    public void Apply_RomanNumeralsAndLetters_RemainUnchanged()
    {
        var sink = new MaterialisingTokenSink();
        _filter.Apply("IVXLCDM\u2160\u2161\u2162abc".AsSpan(), 0, 13, Token.DefaultType, 1, null, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal("IVXLCDM\u2160\u2161\u2162abc", sink.Tokens[0].Text);
    }

    /// <summary>
    /// Tests that an empty token is forwarded without error.
    /// </summary>
    [Fact(DisplayName = "Apply: empty token is forwarded unchanged")]
    public void Apply_EmptyToken_IsForwarded()
    {
        var sink = new MaterialisingTokenSink();
        _filter.Apply(ReadOnlySpan<char>.Empty, 0, 0, Token.DefaultType, 1, null, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal(string.Empty, sink.Tokens[0].Text);
    }

    /// <summary>
    /// Tests that tokens longer than the stack threshold are handled via the pooled buffer path.
    /// </summary>
    [Fact(DisplayName = "Apply: long token with Unicode digits normalises correctly")]
    public void Apply_LongToken_NormalisesCorrectly()
    {
        var prefix = new string('a', 64);
        var suffix = new string('b', 64);
        var input = $"{prefix}\u0661\u0662\u0663{suffix}";

        var sink = new MaterialisingTokenSink();
        _filter.Apply(input.AsSpan(), 0, input.Length, Token.DefaultType, 1, null, sink);

        Assert.Single(sink.Tokens);
        Assert.Equal($"{prefix}123{suffix}", sink.Tokens[0].Text);
    }
}
