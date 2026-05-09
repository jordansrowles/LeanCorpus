using Rowles.LeanLucene.Search;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Unit tests for the bounded and ASCII overloads of <see cref="LevenshteinDistance"/>.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class LevenshteinBoundedTests
{
    // ── ComputeBounded ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Compute Bounded: Returns Distance When Within Max Edits scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Returns Distance When Within Max Edits")]
    public void ComputeBounded_ReturnsDistance_WhenWithinMaxEdits()
    {
        int dist = LevenshteinDistance.ComputeBounded("kitten", "sitting", 5);
        Assert.Equal(3, dist);
    }

    /// <summary>
    /// Verifies the Compute Bounded: Returns Max Edits Plus One When Exceeds Threshold scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Returns MaxEdits Plus One When Exceeds Threshold")]
    public void ComputeBounded_ReturnsMaxEditsPlusOne_WhenExceedsThreshold()
    {
        int dist = LevenshteinDistance.ComputeBounded("kitten", "sitting", 2);
        Assert.Equal(3, dist); // capped at maxEdits + 1 = 3
    }

    /// <summary>
    /// Verifies the Compute Bounded: Empty A Returns B Length When Within Max scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Empty A Returns B Length When Within Max")]
    public void ComputeBounded_EmptyA_ReturnsBLength_WhenWithinMax()
    {
        int dist = LevenshteinDistance.ComputeBounded("", "abc", 5);
        Assert.Equal(3, dist);
    }

    /// <summary>
    /// Verifies the Compute Bounded: Empty A Returns Max Plus One When Exceeds Max scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Empty A Returns MaxPlusOne When Exceeds Max")]
    public void ComputeBounded_EmptyA_ReturnsMaxPlusOne_WhenExceedsMax()
    {
        int dist = LevenshteinDistance.ComputeBounded("", "abcdef", 3);
        Assert.Equal(4, dist); // 6 > 3, return 4
    }

    /// <summary>
    /// Verifies the Compute Bounded: Empty B Returns A Length When Within Max scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Empty B Returns A Length When Within Max")]
    public void ComputeBounded_EmptyB_ReturnsALength_WhenWithinMax()
    {
        int dist = LevenshteinDistance.ComputeBounded("ab", "", 5);
        Assert.Equal(2, dist);
    }

    /// <summary>
    /// Verifies the Compute Bounded: Length Difference Exceeds Max Returns Max Plus One scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Length Difference Exceeds Max Returns MaxPlusOne")]
    public void ComputeBounded_LengthDifferenceExceedsMax_ReturnsMaxPlusOne()
    {
        int dist = LevenshteinDistance.ComputeBounded("a", "abcdef", 2);
        Assert.Equal(3, dist); // |1-6| = 5 > 2, returns 3
    }

    /// <summary>
    /// Verifies the Compute Bounded: Identical Strings Returns Zero scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Identical Strings Returns Zero")]
    public void ComputeBounded_IdenticalStrings_ReturnsZero()
    {
        int dist = LevenshteinDistance.ComputeBounded("hello", "hello", 3);
        Assert.Equal(0, dist);
    }

    /// <summary>
    /// Verifies the Compute Bounded: Early Termination On Row Min scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeBounded: Early Termination On Row Min")]
    public void ComputeBounded_EarlyTermination_OnRowMin()
    {
        // Strings completely different and long -- exercises early termination path
        var a = new string('x', 50);
        var b = new string('y', 50);
        int dist = LevenshteinDistance.ComputeBounded(a, b, 5);
        Assert.Equal(6, dist); // all chars different, capped at 6
    }

    // ── ComputeAscii ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Compute Ascii: Returns Correct Distance For Ascii scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAscii: Returns Correct Distance For Ascii")]
    public void ComputeAscii_ReturnsCorrectDistance_ForAscii()
    {
        var a = System.Text.Encoding.ASCII.GetBytes("kitten");
        var b = System.Text.Encoding.ASCII.GetBytes("sitting");
        int dist = LevenshteinDistance.ComputeAscii(a, b);
        Assert.Equal(3, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii: Empty A Returns B Length scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAscii: Empty A Returns B Length")]
    public void ComputeAscii_EmptyA_ReturnsBLength()
    {
        var b = System.Text.Encoding.ASCII.GetBytes("abc");
        int dist = LevenshteinDistance.ComputeAscii([], b);
        Assert.Equal(3, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii: Empty B Returns A Length scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAscii: Empty B Returns A Length")]
    public void ComputeAscii_EmptyB_ReturnsALength()
    {
        var a = System.Text.Encoding.ASCII.GetBytes("abc");
        int dist = LevenshteinDistance.ComputeAscii(a, []);
        Assert.Equal(3, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii: Non Ascii Byte Returns Minus One scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAscii: Non Ascii Byte Returns Minus One")]
    public void ComputeAscii_NonAsciiByte_ReturnsMinusOne()
    {
        ReadOnlySpan<byte> a = [0x68, 0x65, 0xC3]; // "he" + non-ASCII
        ReadOnlySpan<byte> b = [0x68, 0x65, 0x6C]; // "hel"
        int dist = LevenshteinDistance.ComputeAscii(a, b);
        Assert.Equal(-1, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii: Non Ascii In B Returns Minus One scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAscii: Non Ascii In B Returns Minus One")]
    public void ComputeAscii_NonAsciiInB_ReturnsMinusOne()
    {
        ReadOnlySpan<byte> a = [0x68, 0x65, 0x6C]; // "hel"
        ReadOnlySpan<byte> b = [0x68, 0x65, 0xC3]; // "he" + non-ASCII
        int dist = LevenshteinDistance.ComputeAscii(a, b);
        Assert.Equal(-1, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii: Identical Bytes Returns Zero scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAscii: Identical Bytes Returns Zero")]
    public void ComputeAscii_IdenticalBytes_ReturnsZero()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("hello");
        Assert.Equal(0, LevenshteinDistance.ComputeAscii(bytes, bytes));
    }

    // ── ComputeAsciiBounded ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Returns Distance When Within Max scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Returns Distance When Within Max")]
    public void ComputeAsciiBounded_ReturnsDistance_WhenWithinMax()
    {
        var a = System.Text.Encoding.ASCII.GetBytes("abc");
        var b = System.Text.Encoding.ASCII.GetBytes("axc");
        int dist = LevenshteinDistance.ComputeAsciiBounded(a, b, 2);
        Assert.Equal(1, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Returns Max Plus One When Exceeds Threshold scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Returns MaxPlusOne When Exceeds Threshold")]
    public void ComputeAsciiBounded_ReturnsMaxPlusOne_WhenExceedsThreshold()
    {
        var a = System.Text.Encoding.ASCII.GetBytes("kitten");
        var b = System.Text.Encoding.ASCII.GetBytes("sitting");
        int dist = LevenshteinDistance.ComputeAsciiBounded(a, b, 2);
        Assert.Equal(3, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Empty A Returns B Length Within Max scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Empty A Returns B Length Within Max")]
    public void ComputeAsciiBounded_EmptyA_ReturnsBLength_WithinMax()
    {
        var b = System.Text.Encoding.ASCII.GetBytes("ab");
        int dist = LevenshteinDistance.ComputeAsciiBounded([], b, 5);
        Assert.Equal(2, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Empty A Returns Max Plus One When Exceeds Max scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Empty A Returns MaxPlusOne When Exceeds Max")]
    public void ComputeAsciiBounded_EmptyA_ReturnsMaxPlusOne_WhenExceedsMax()
    {
        var b = System.Text.Encoding.ASCII.GetBytes("abcde");
        int dist = LevenshteinDistance.ComputeAsciiBounded([], b, 3);
        Assert.Equal(4, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Empty B Returns A Length Within Max scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Empty B Returns A Length Within Max")]
    public void ComputeAsciiBounded_EmptyB_ReturnsALength_WithinMax()
    {
        var a = System.Text.Encoding.ASCII.GetBytes("hi");
        int dist = LevenshteinDistance.ComputeAsciiBounded(a, [], 5);
        Assert.Equal(2, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Length Difference Exceeds Max Returns Max Plus One scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Length Difference Exceeds Max Returns MaxPlusOne")]
    public void ComputeAsciiBounded_LengthDifferenceExceedsMax_ReturnsMaxPlusOne()
    {
        var a = System.Text.Encoding.ASCII.GetBytes("a");
        var b = System.Text.Encoding.ASCII.GetBytes("abcdef");
        int dist = LevenshteinDistance.ComputeAsciiBounded(a, b, 2);
        Assert.Equal(3, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Non Ascii Returns Minus One scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Non Ascii Returns Minus One")]
    public void ComputeAsciiBounded_NonAscii_ReturnsMinusOne()
    {
        ReadOnlySpan<byte> a = [0x68, 0x65, 0xC3];
        ReadOnlySpan<byte> b = [0x68, 0x65, 0x6C];
        int dist = LevenshteinDistance.ComputeAsciiBounded(a, b, 5);
        Assert.Equal(-1, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Non Ascii In B Returns Minus One scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Non Ascii In B Returns Minus One")]
    public void ComputeAsciiBounded_NonAsciiInB_ReturnsMinusOne()
    {
        ReadOnlySpan<byte> a = [0x68, 0x65, 0x6C];
        ReadOnlySpan<byte> b = [0x68, 0x65, 0xC3];
        int dist = LevenshteinDistance.ComputeAsciiBounded(a, b, 5);
        Assert.Equal(-1, dist);
    }

    /// <summary>
    /// Verifies the Compute Ascii Bounded: Early Termination On Row Min scenario.
    /// </summary>
    [Fact(DisplayName = "ComputeAsciiBounded: Early Termination On Row Min")]
    public void ComputeAsciiBounded_EarlyTermination_OnRowMin()
    {
        var a = System.Text.Encoding.ASCII.GetBytes("aaaaaaaaaa");
        var b = System.Text.Encoding.ASCII.GetBytes("bbbbbbbbbb");
        int dist = LevenshteinDistance.ComputeAsciiBounded(a, b, 3);
        Assert.Equal(4, dist);
    }
}
