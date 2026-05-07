using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Tests for the SIMD-accelerated sorted array intersection used in Boolean AND queries.
/// Both scalar and vectorised code paths are exercised depending on the host hardware.
/// </summary>
public sealed class SimdIntersectionTests
{
    /// <summary>
    /// Verifies the Intersect: Identical Arrays Returns All scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect: Identical Arrays Returns All")]
    public void Intersect_IdenticalArrays_ReturnsAll()
    {
        // Arrange
        int[] a = [1, 2, 3, 4, 5];
        int[] b = [1, 2, 3, 4, 5];
        Span<int> result = stackalloc int[a.Length];

        // Act
        int count = SimdIntersection.Intersect(a, b, result);

        // Assert
        Assert.Equal(a.Length, count);
        Assert.Equal(a, result[..count].ToArray());
    }

    /// <summary>
    /// Verifies the Intersect: No Overlap Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect: No Overlap Returns Empty")]
    public void Intersect_NoOverlap_ReturnsEmpty()
    {
        // Arrange
        int[] a = [1, 3, 5];
        int[] b = [2, 4, 6];
        Span<int> result = stackalloc int[Math.Min(a.Length, b.Length)];

        // Act
        int count = SimdIntersection.Intersect(a, b, result);

        // Assert
        Assert.Equal(0, count);
    }

    /// <summary>
    /// Verifies the Intersect: Partial Overlap Returns Common scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect: Partial Overlap Returns Common")]
    public void Intersect_PartialOverlap_ReturnsCommon()
    {
        // Arrange
        int[] a = [1, 2, 3, 5, 8];
        int[] b = [2, 5, 7, 8, 9];
        int[] expected = [2, 5, 8];
        Span<int> result = stackalloc int[Math.Min(a.Length, b.Length)];

        // Act
        int count = SimdIntersection.Intersect(a, b, result);

        // Assert
        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, result[..count].ToArray());
    }

    /// <summary>
    /// Verifies the Intersect: Empty Input Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect: Empty Input Returns Empty")]
    public void Intersect_EmptyInput_ReturnsEmpty()
    {
        // Arrange
        ReadOnlySpan<int> empty = ReadOnlySpan<int>.Empty;
        int[] nonEmpty = [10, 20, 30];
        Span<int> result = stackalloc int[nonEmpty.Length];

        // Act
        int countEmptyFirst = SimdIntersection.Intersect(empty, nonEmpty, result);
        int countEmptySecond = SimdIntersection.Intersect(nonEmpty, empty, result);

        // Assert
        Assert.Equal(0, countEmptyFirst);
        Assert.Equal(0, countEmptySecond);
    }

    /// <summary>
    /// Verifies the Intersect: Large Arrays Correct Result scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect: Large Arrays Correct Result")]
    public void Intersect_LargeArrays_CorrectResult()
    {
        // Arrange — 10K even numbers vs 10K multiples of 3
        const int size = 10_000;
        var evens = new int[size];
        var multiplesOf3 = new int[size];

        for (int i = 0; i < size; i++)
        {
            evens[i] = (i + 1) * 2;         // 2, 4, 6, …, 20000
            multiplesOf3[i] = (i + 1) * 3;   // 3, 6, 9, …, 30000
        }

        // Reference result via HashSet
        var setA = new HashSet<int>(evens);
        var expectedList = new List<int>();
        foreach (int m in multiplesOf3)
        {
            if (setA.Contains(m))
                expectedList.Add(m);
        }
        var expected = expectedList.ToArray();

        var result = new int[Math.Min(evens.Length, multiplesOf3.Length)];

        // Act
        int count = SimdIntersection.Intersect(evens, multiplesOf3, result);

        // Assert
        Assert.Equal(expected.Length, count);
        Assert.Equal(expected, result[..count]);
    }

    /// <summary>
    /// Verifies the Intersect Count: Matches Intersect Length scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect Count: Matches Intersect Length")]
    public void IntersectCount_MatchesIntersectLength()
    {
        // Arrange
        int[] a = [1, 2, 3, 5, 8, 13, 21, 34, 55, 89];
        int[] b = [2, 3, 5, 7, 11, 13, 17, 21, 34, 100];
        var result = new int[Math.Min(a.Length, b.Length)];

        // Act
        int intersectLength = SimdIntersection.Intersect(a, b, result);
        int intersectCount = SimdIntersection.IntersectCount(a, b);

        // Assert
        Assert.Equal(intersectLength, intersectCount);
    }

    /// <summary>
    /// Verifies the Intersect: Single Element Match scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect: Single Element Match")]
    public void Intersect_SingleElement_Match()
    {
        // Arrange
        int[] a = [42];
        int[] b = [42];
        Span<int> result = stackalloc int[1];

        // Act
        int count = SimdIntersection.Intersect(a, b, result);

        // Assert
        Assert.Equal(1, count);
        Assert.Equal(42, result[0]);
    }

    /// <summary>
    /// Verifies the Intersect: Single Element No Match scenario.
    /// </summary>
    [Fact(DisplayName = "Intersect: Single Element No Match")]
    public void Intersect_SingleElement_NoMatch()
    {
        // Arrange
        int[] a = [42];
        int[] b = [43];
        Span<int> result = stackalloc int[1];

        // Act
        int count = SimdIntersection.Intersect(a, b, result);

        // Assert
        Assert.Equal(0, count);
    }
}
