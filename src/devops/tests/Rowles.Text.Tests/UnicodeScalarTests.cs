using System.Buffers;
using System.Globalization;
using System.Text;
using Rowles.LeanCorpus.Analysis.Tokenisers.Japanese;

namespace Rowles.Text.Tests;

[Category(TestCategory.Unit)]
[Area(TestArea.Tokenisers)]
[Area(TestArea.Filters)]
public sealed class UnicodeScalarTests
{
    private const string Upper = "\U00010400";
    private const string Lower = "\U00010428";
    private const string Digit = "\U000104A3";
    private const string Mark = "\U000101FD";
    private const string Emoji = "\U0001F600";

    [Fact]
    public void Shared_decoder_counts_and_boundaries_use_the_same_invalid_unit_policy()
    {
        string input = "A" + Upper + "\uD800B\uDC00";
        var units = OracleUnits(input);
        foreach (var (start, width, valid, rune) in units)
        {
            Assert.Equal(valid, UnicodeTokenisation.TryDecodeRuneAt(input, start, out var forward, out int forwardWidth));
            Assert.Equal(valid, UnicodeTokenisation.TryDecodeRuneBefore(input, start + width, out var reverse, out int reverseWidth));
            Assert.Equal(width, forwardWidth);
            Assert.Equal(width, reverseWidth);
            if (valid) { Assert.Equal(rune, forward); Assert.Equal(rune, reverse); }
        }
        Assert.Equal(units.Count, UnicodeTokenisation.CountScalarUnits(input));
        for (int count = 0; count <= units.Count + 1; count++)
        {
            Assert.Equal(Math.Min(count, units.Count), UnicodeTokenisation.CountScalarUnitsUpTo(input, count));
            Assert.Equal(OracleBoundary(input, count), UnicodeTokenisation.FindUtf16BoundaryAfterScalarUnits(input, count));
        }
    }

    [Theory]
    [InlineData("\U00010400", "\U00010400", 0, 2)]
    [InlineData("A\U00010400B", "A\U00010400B", 0, 4)]
    [InlineData("3.\U00010400!9", "\U00010400", 2, 4)]
    public void Letter_tokeniser_recognises_supplementary_letters(string input, string text, int start, int end)
    {
        var tokeniser = new LetterTokeniser();
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise(input, sink);
        Assert.Equal((text, start, end), Projection(Assert.Single(sink.Tokens)));
        var offsets = new List<(int Start, int End)>();
        tokeniser.TokeniseOffsets(input, offsets);
        Assert.Equal(sink.Tokens.Select(t => (t.StartOffset, t.EndOffset)), offsets);
    }

    [Fact]
    public void Letter_tokeniser_treats_invalid_units_as_delimiters()
    {
        var sink = new MaterialisingTokenSink();
        new LetterTokeniser().Tokenise("A\uD800" + Upper + "\uDC00B", sink);
        Assert.Equal(new[] { ("A", 0, 1), (Upper, 2, 4), ("B", 5, 6) }, sink.Tokens.Select(Projection));
    }

    [Fact]
    public void Ngrams_have_exact_scalar_sizes_and_utf16_offsets()
    {
        Assert.Equal(new[] { ("A", 0, 1), ("A" + Upper, 0, 3), (Upper, 1, 3), (Upper + "B", 1, 4), ("B", 3, 4) },
            NGrams(new NGramTokeniser(1, 2), "A" + Upper + "B"));
        Assert.Equal(new[] { (Upper, 0, 2), (Upper + "A", 0, 3), (Upper + "AB", 0, 4) },
            EdgeGrams(new EdgeNGramTokeniser(1, 3), Upper + "AB"));
        string words = Upper + "A\u2003B" + Lower;
        var ngrams = NGrams(new NGramTokeniser(1, 2, true), words);
        var edges = EdgeGrams(new EdgeNGramTokeniser(1, 3), words);
        Assert.Equal(new[] { (Upper, 0, 2), (Upper + "A", 0, 3), ("A", 2, 3), ("B", 4, 5), ("B" + Lower, 4, 7), (Lower, 5, 7) }, ngrams);
        Assert.Equal(new[] { (Upper, 0, 2), (Upper + "A", 0, 3), ("B", 4, 5), ("B" + Lower, 4, 7) }, edges);
    }

    [Fact]
    public void Length_and_truncation_count_scalars_inclusively_and_preserve_metadata()
    {
        string input = "A" + Upper + "B";
        Assert.Single(Apply(new LengthFilter(3, 3), input));
        Assert.Empty(Apply(new LengthFilter(4, 4), input));
        Assert.Empty(Apply(new LengthFilter(1, 2), input));
        var token = Assert.Single(Apply(new TruncateTokenFilter(2), input));
        Assert.Equal("A" + Upper, token.Text);
        Assert.Equal(7, token.StartOffset);
        Assert.Equal(27, token.EndOffset);
        Assert.Equal("custom", token.Type);
        Assert.Equal(3, token.PositionIncrement);
        Assert.Equal(4, token.PositionLength);
        Assert.Equal(new byte[] { 1, 2 }, token.Payload);
    }

    [Fact]
    public void Length_bounds_with_no_guaranteed_utf16_range_still_count_safely()
    {
        Assert.Empty(Apply(new LengthFilter(int.MaxValue, int.MaxValue), Upper));
        Assert.Empty(Apply(new LengthFilter(2, 2), Upper));
        Assert.Single(Apply(new LengthFilter(2, 2), "\uD800A"));
        Assert.Single(Apply(new LengthFilter(0, 1), ""));
    }

    [Theory]
    [InlineData("A\U0001F600\U00010400", "\U00010400\U0001F600A")]
    [InlineData("A\uD800B", "B\uD800A")]
    public void Reversal_preserves_scalar_encoding_and_invalid_units(string input, string expected)
    {
        Assert.Equal(expected, Assert.Single(Apply(new ReverseStringFilter(), input)).Text);
        if (IsValid(input)) AssertValid(expected);
    }

    [Fact]
    public void Reversal_can_join_invalid_units_into_a_valid_pair_without_throwing()
    {
        const string broken = "\uDC00\uD800";
        string reversed = Assert.Single(Apply(new ReverseStringFilter(), broken)).Text;
        Assert.Equal("\uD800\uDC00", reversed);
        AssertValid(reversed);
        Assert.Equal(reversed, Assert.Single(Apply(new ReverseStringFilter(), reversed)).Text);
    }

    [Theory]
    [InlineData("\U000104A3", "3")]
    [InlineData("a١\U000104A3９b", "a139b")]
    [InlineData("a\uD800\U000104A3\uDC00b", "a\uD8003\uDC00b")]
    [InlineData("abc123", "abc123")]
    public void Decimal_digits_normalise_scalars_and_preserve_source_offsets(string input, string expected)
    {
        var token = Assert.Single(Apply(new DecimalDigitFilter(), input));
        Assert.Equal(expected, token.Text);
        Assert.Equal((7, 27), (token.StartOffset, token.EndOffset));
    }

    [Fact]
    public void Supplementary_digit_then_truncation_keeps_exact_original_source_range()
    {
        var digit = Assert.Single(Apply(new DecimalDigitFilter(), Digit, 0, 2));
        Assert.Equal("3", digit.Text);
        Assert.Equal((0, 2), (digit.StartOffset, digit.EndOffset));
        var sink = new MaterialisingTokenSink();
        new TruncateTokenFilter(1).Apply(digit.Text, digit.StartOffset, digit.EndOffset,
            digit.Type, digit.PositionIncrement, digit.Payload, sink);
        Assert.Equal(("3", 0, 2), Projection(Assert.Single(sink.Tokens)));
    }

    [Theory]
    [InlineData("a\U00010400b", "a", "\U00010400b")]
    [InlineData("\U00010400b", "\U00010400b", null)]
    [InlineData("\U00010400\U00010400\U00010428", "\U00010400", "\U00010400\U00010428")]
    [InlineData("a\U000104A3", "a", "\U000104A3")]
    [InlineData("\U000104A3a", "\U000104A3", "a")]
    public void Word_delimiter_splits_only_at_scalar_starts(string input, string first, string? second)
    {
        var tokens = Apply(new WordDelimiterFilter(), input);
        Assert.Equal(second is null ? new[] { first } : new[] { first, second }, tokens.Select(t => t.Text));
        Assert.Equal((7, 7 + first.Length), (tokens[0].StartOffset, tokens[0].EndOffset));
        if (second is not null) Assert.Equal((7 + first.Length, 7 + input.Length), (tokens[1].StartOffset, tokens[1].EndOffset));
        foreach (var token in tokens) AssertValid(token.Text);
    }

    [Fact]
    public void Word_delimiter_custom_bmp_delimiters_and_catenation_preserve_complete_slices()
    {
        string input = Upper + "|" + Lower;
        var tokens = Apply(new WordDelimiterFilter(['|']) { CatenateWords = true, PreserveOriginal = true }, input);
        Assert.Equal(new[] { Upper, Lower, Upper + Lower, input }, tokens.Select(t => t.Text));
        Assert.Equal((7, 12), (tokens[2].StartOffset, tokens[2].EndOffset));
        Assert.Equal((7, 27), (tokens[3].StartOffset, tokens[3].EndOffset));
    }

    [Theory]
    [InlineData("é e\u0301", "e e")]
    [InlineData("A\U000101FD", "A")]
    [InlineData("ø", "ø")]
    [InlineData("A\uD800é", "A\uD800e")]
    public void Accent_folding_normalises_valid_runs_and_removes_supplementary_marks(string input, string expected)
    {
        var token = Assert.Single(Apply(new AccentFoldingFilter(), input));
        Assert.Equal(expected, token.Text);
        Assert.Equal((7, 27), (token.StartOffset, token.EndOffset));
        Assert.Equal(expected, AccentFoldingFilter.Fold(input));
        if (expected == input) Assert.Same(input, AccentFoldingFilter.Fold(input));
    }

    [Fact]
    public void Long_transforms_exercise_pooled_buffers()
    {
        string input = string.Concat(Enumerable.Repeat("é" + Mark + Digit + Emoji, 100));
        Assert.Equal(string.Concat(Enumerable.Repeat("e" + Digit + Emoji, 100)), Assert.Single(Apply(new AccentFoldingFilter(), input)).Text);
        Assert.Equal(OracleDigits(input), Assert.Single(Apply(new DecimalDigitFilter(), input)).Text);
        Assert.Equal(OracleReverse(input), Assert.Single(Apply(new ReverseStringFilter(), input)).Text);
    }

    [Fact]
    public void Japanese_punctuation_distinguishes_supplementary_categories()
    {
        Assert.True(JapaneseViterbi.IsPunctuation("\U00010100"));
        Assert.True(JapaneseViterbi.IsPunctuation("\u2003"));
        Assert.False(JapaneseViterbi.IsPunctuation(Emoji));
        Assert.False(JapaneseViterbi.IsPunctuation("\uD800\uDC00"));
        Assert.False(JapaneseViterbi.IsPunctuation("\uD800"));
    }

    [Fact]
    public void Scalar_filter_chain_preserves_graph_metadata_after_text_shrinks()
    {
        var token = Assert.Single(Apply(new DecimalDigitFilter(), Digit + "é"));
        foreach (ISpanTokenFilter filter in new ISpanTokenFilter[] { new AccentFoldingFilter(), new TruncateTokenFilter(1), new ReverseStringFilter(), new LengthFilter(1, 1) })
        {
            var sink = new MaterialisingTokenSink();
            filter.Apply(token.Text, token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.PositionLength, token.Payload, sink);
            token = Assert.Single(sink.Tokens);
        }
        Assert.Equal("3", token.Text);
        Assert.Equal((7, 27, "custom", 3, 4), (token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.PositionLength));
        Assert.Equal(new byte[] { 1, 2 }, token.Payload);
    }

    [Fact]
    public void Deterministic_scalar_properties_use_independent_oracles()
    {
        string[] alphabet = ["a", "Z", "7", "-", " ", "\u2003", "é", "١", "\u0301", Upper, Lower, Digit, Mark, "\U00010100", Emoji, "\uD800", "\uDC00"];
        var random = new Random(36036);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            string input = string.Concat(Enumerable.Range(0, random.Next(0, 32)).Select(_ => alphabet[random.Next(iteration % 2 == 0 ? alphabet.Length - 2 : alphabet.Length)]));
            var units = OracleUnits(input);
            Assert.Equal(units.Count, UnicodeTokenisation.CountScalarUnits(input));
            int maximum = random.Next(1, 8);
            int minimumLength = random.Next(0, 8);
            int maximumLength = random.Next(Math.Max(1, minimumLength), 12);
            Assert.Equal(units.Count >= minimumLength && units.Count <= maximumLength,
                Apply(new LengthFilter(minimumLength, maximumLength), input, 0, input.Length).Count == 1);
            string truncated = Assert.Single(Apply(new TruncateTokenFilter(maximum), input, 0, input.Length)).Text;
            Assert.Equal(input[..OracleBoundary(input, maximum)], truncated);
            Assert.True(OracleUnits(truncated).Count <= maximum);
            string reversed = Assert.Single(Apply(new ReverseStringFilter(), input, 0, input.Length)).Text;
            Assert.Equal(OracleReverse(input), reversed);
            // Invalid units can form a new valid pair when reversed. The specified
            // scalar algorithm cannot then recover the original broken encoding.
            if (OracleUnits(reversed).Count == units.Count)
                Assert.Equal(input, Assert.Single(Apply(new ReverseStringFilter(), reversed)).Text);
            Assert.Equal(OracleDigits(input), Assert.Single(Apply(new DecimalDigitFilter(), input, 0, input.Length)).Text);
            var ngrams = NGrams(new NGramTokeniser(1, maximum, iteration % 2 == 0), input);
            var edges = EdgeGrams(new EdgeNGramTokeniser(1, maximum), input);
            foreach (var (_, start, end) in ngrams.Concat(edges))
            {
                Assert.InRange(start, 0, input.Length);
                Assert.InRange(end, start, input.Length);
                Assert.InRange(OracleUnits(input[start..end]).Count, 1, maximum);
                Assert.Contains(start, units.Select(u => u.Start).Append(input.Length));
                Assert.Contains(end, units.Select(u => u.Start).Append(input.Length));
            }
            if (IsValid(input))
            {
                AssertValid(reversed);
                AssertValid(truncated);
                foreach (var (text, _, _) in ngrams.Concat(edges)) AssertValid(text);
                var letters = new MaterialisingTokenSink();
                new LetterTokeniser().Tokenise(input, letters);
                foreach (var token in letters.Tokens) AssertTokenBounds(token, input);
                foreach (ISpanTokenFilter filter in new ISpanTokenFilter[] { new LengthFilter(0, int.MaxValue), new DecimalDigitFilter(), new AccentFoldingFilter(), new WordDelimiterFilter() })
                    foreach (var token in Apply(filter, input, 0, input.Length)) AssertTokenBounds(token, input);
            }
        }
    }

    private static List<Token> Apply(ISpanTokenFilter filter, string input, int start = 7, int end = 27)
    {
        var sink = new MaterialisingTokenSink();
        filter.Apply(input, start, end, "custom", 3, 4, [1, 2], sink);
        return sink.Tokens;
    }

    private static (string Text, int Start, int End) Projection(Token token) => (token.Text, token.StartOffset, token.EndOffset);
    private static List<(string Text, int Start, int End)> NGrams(NGramTokeniser tokeniser, string input)
    {
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise(input, sink);
        var enumerated = new List<(string, int, int)>();
        foreach (var token in tokeniser.EnumerateTokens(input)) enumerated.Add((token.Text.ToString(), token.StartOffset, token.EndOffset));
        Assert.Equal(sink.Tokens.Select(Projection), enumerated);
        return enumerated;
    }
    private static List<(string Text, int Start, int End)> EdgeGrams(EdgeNGramTokeniser tokeniser, string input)
    {
        var sink = new MaterialisingTokenSink();
        tokeniser.Tokenise(input, sink);
        var enumerated = new List<(string, int, int)>();
        foreach (var token in tokeniser.EnumerateTokens(input)) enumerated.Add((token.Text.ToString(), token.StartOffset, token.EndOffset));
        Assert.Equal(sink.Tokens.Select(Projection), enumerated);
        return enumerated;
    }
    private static List<(int Start, int Width, bool Valid, Rune Rune)> OracleUnits(string input)
    {
        var units = new List<(int, int, bool, Rune)>();
        for (int position = 0; position < input.Length;)
        {
            bool valid = Rune.DecodeFromUtf16(input.AsSpan(position), out var rune, out int width) == OperationStatus.Done;
            if (!valid) width = 1;
            units.Add((position, width, valid, rune));
            position += width;
        }
        return units;
    }
    private static int OracleBoundary(string input, int maximum) => OracleUnits(input).Take(maximum).Sum(u => u.Width);
    private static bool IsValid(string input) => OracleUnits(input).All(u => u.Valid);
    private static void AssertValid(string input)
    {
        Assert.True(IsValid(input));
#if NET11_0_OR_GREATER
        Assert.True(System.Text.Unicode.Utf16.IsValid(input));
#endif
    }
    private static void AssertTokenBounds(Token token, string input)
    {
        AssertValid(token.Text);
        Assert.InRange(token.StartOffset, 0, input.Length);
        Assert.InRange(token.EndOffset, token.StartOffset, input.Length);
        var boundaries = OracleUnits(input).Select(u => u.Start).Append(input.Length);
        Assert.Contains(token.StartOffset, boundaries);
        Assert.Contains(token.EndOffset, boundaries);
    }
    private static string OracleReverse(string input)
    {
        var result = new StringBuilder();
        for (int end = input.Length; end > 0;)
        {
            bool valid = Rune.DecodeLastFromUtf16(input.AsSpan(0, end), out var rune, out int width) == OperationStatus.Done;
            if (!valid) width = 1;
            if (valid) result.Append(rune.ToString());
            else result.Append(input[end - 1]);
            end -= width;
        }
        return result.ToString();
    }
    private static string OracleDigits(string input)
    {
        var result = new StringBuilder();
        foreach (var unit in OracleUnits(input))
        {
            if (unit.Valid && Rune.GetUnicodeCategory(unit.Rune) == UnicodeCategory.DecimalDigitNumber)
                result.Append((char)('0' + (int)Rune.GetNumericValue(unit.Rune)));
            else result.Append(input.AsSpan(unit.Start, unit.Width));
        }
        return result.ToString();
    }
}
