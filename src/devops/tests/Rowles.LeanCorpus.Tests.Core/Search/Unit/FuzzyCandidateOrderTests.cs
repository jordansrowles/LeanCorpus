using System.Text;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Fuzzy candidate selection breaks distance ties by UTF-8 byte order. The allocation-free comparer
/// must agree with comparing the encoded bytes, including where surrogate pairs meet U+E000 to U+FFFF.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class FuzzyCandidateOrderTests
{
    private static readonly string[] Alphabet =
    [
        "a", "b", "z", "é", "߿", "ࠀ", "퟿", "", "", "￯", "￿",
        "\U00010000", "\U0001f600", "\U0010ffff"
    ];

    [Theory]
    [InlineData("a", "b")]
    [InlineData("a", "ab")]
    [InlineData("￿", "\U00010000")]
    [InlineData("", "\U0001f600")]
    [InlineData("퟿", "")]
    [InlineData("x\U0001f600", "x￯")]
    public void MatchesUtf8ByteOrderForFixedPairs(string left, string right)
    {
        Assert.Equal(Utf8Sign(left, right), Math.Sign(FuzzyQueryExecutionContext.CompareUtf8Order(left, right)));
        Assert.Equal(Utf8Sign(right, left), Math.Sign(FuzzyQueryExecutionContext.CompareUtf8Order(right, left)));
    }

    [Fact]
    public void MatchesUtf8ByteOrderForGeneratedStrings()
    {
        var random = new Random(20261118); // NOSONAR: deterministic test data, not security-sensitive.
        for (int i = 0; i < 5_000; i++)
        {
            string left = Generate(random), right = Generate(random);
            Assert.Equal(Utf8Sign(left, right), Math.Sign(FuzzyQueryExecutionContext.CompareUtf8Order(left, right)));
        }
    }

    private static string Generate(Random random)
    {
        var builder = new StringBuilder();
        int length = random.Next(0, 5);
        for (int i = 0; i < length; i++)
            builder.Append(Alphabet[random.Next(Alphabet.Length)]);
        return builder.ToString();
    }

    private static int Utf8Sign(string left, string right)
        => Math.Sign(Encoding.UTF8.GetBytes(left).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(right)));
}
