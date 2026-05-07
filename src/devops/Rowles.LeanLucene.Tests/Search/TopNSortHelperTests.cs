using Rowles.LeanLucene.Search.Scoring;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Unit tests for <see cref="TopNSortHelper"/>.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class TopNSortHelperTests
{
    private static ScoreDoc[] MakeDocs(int count)
        => Enumerable.Range(0, count).Select(i => new ScoreDoc(i, 1.0f)).ToArray();

    // ── Numeric (double) overload ─────────────────────────────────────────────

    /// <summary>
    /// Verifies the Select Top N Double: Returns All When Top N Exceeds Count scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN Double: Returns All When TopN Exceeds Count")]
    public void SelectTopN_Double_ReturnsAll_WhenTopNExceedsCount()
    {
        var docs = MakeDocs(3);
        var keys = new double[] { 3.0, 1.0, 2.0 };

        var result = TopNSortHelper.SelectTopN(docs, keys, 10, descending: false);

        Assert.Equal(3, result.Length);
    }

    /// <summary>
    /// Verifies the Select Top N Double: Returns Top N Ascending scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN Double: Returns Top N Ascending")]
    public void SelectTopN_Double_ReturnsTopN_Ascending()
    {
        var docs = MakeDocs(5);
        var keys = new double[] { 5.0, 3.0, 1.0, 4.0, 2.0 };

        var result = TopNSortHelper.SelectTopN(docs, keys, 3, descending: false);

        Assert.Equal(3, result.Length);
        // Results should correspond to the smallest 3 keys
        var resultDocs = result.Select(d => d.DocId).ToHashSet();
        // keys: doc0=5, doc1=3, doc2=1, doc3=4, doc4=2 => smallest 3 are docs 2,4,1
        Assert.Contains(2, resultDocs); // key=1
        Assert.Contains(4, resultDocs); // key=2
        Assert.Contains(1, resultDocs); // key=3
    }

    /// <summary>
    /// Verifies the Select Top N Double: Returns Top N Descending scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN Double: Returns Top N Descending")]
    public void SelectTopN_Double_ReturnsTopN_Descending()
    {
        var docs = MakeDocs(5);
        var keys = new double[] { 5.0, 3.0, 1.0, 4.0, 2.0 };

        var result = TopNSortHelper.SelectTopN(docs, keys, 2, descending: true);

        Assert.Equal(2, result.Length);
        // Largest 2: doc0=5, doc3=4
        var resultDocs = result.Select(d => d.DocId).ToHashSet();
        Assert.Contains(0, resultDocs);
        Assert.Contains(3, resultDocs);
    }

    /// <summary>
    /// Verifies the Select Top N Double: All When Top N Equals Count Ascending scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN Double: All When TopN Equals Count Ascending")]
    public void SelectTopN_Double_All_WhenTopNEqualsCount_Ascending()
    {
        var docs = MakeDocs(4);
        var keys = new double[] { 4.0, 2.0, 3.0, 1.0 };

        var result = TopNSortHelper.SelectTopN(docs, keys, 4, descending: false);

        // Sorted ascending by key: doc3(1), doc1(2), doc2(3), doc0(4)
        Assert.Equal(4, result.Length);
        Assert.Equal(3, result[0].DocId);
        Assert.Equal(1, result[1].DocId);
        Assert.Equal(2, result[2].DocId);
        Assert.Equal(0, result[3].DocId);
    }

    /// <summary>
    /// Verifies the Select Top N Double: All When Top N Equals Count Descending scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN Double: All When TopN Equals Count Descending")]
    public void SelectTopN_Double_All_WhenTopNEqualsCount_Descending()
    {
        var docs = MakeDocs(3);
        var keys = new double[] { 1.0, 3.0, 2.0 };

        var result = TopNSortHelper.SelectTopN(docs, keys, 3, descending: true);

        Assert.Equal(3, result.Length);
        // Sorted descending: doc1(3), doc2(2), doc0(1)
        Assert.Equal(1, result[0].DocId);
        Assert.Equal(2, result[1].DocId);
        Assert.Equal(0, result[2].DocId);
    }

    /// <summary>
    /// Verifies the Select Top N Double: Heap Path With Replacement scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN Double: Heap Path With Replacement")]
    public void SelectTopN_Double_HeapPathWithReplacement()
    {
        // topN=2 with 6 elements, exercises the heap replacement loop
        var docs = MakeDocs(6);
        var keys = new double[] { 3.0, 1.0, 5.0, 2.0, 6.0, 4.0 };

        var result = TopNSortHelper.SelectTopN(docs, keys, 2, descending: true);

        Assert.Equal(2, result.Length);
        var resultDocs = result.Select(d => d.DocId).ToHashSet();
        Assert.Contains(4, resultDocs); // key=6
        Assert.Contains(2, resultDocs); // key=5
    }

    // ── String overload ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Select Top N String: Returns All When Top N Exceeds Count scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN String: Returns All When TopN Exceeds Count")]
    public void SelectTopN_String_ReturnsAll_WhenTopNExceedsCount()
    {
        var docs = MakeDocs(3);
        var keys = new string[] { "cherry", "apple", "banana" };

        var result = TopNSortHelper.SelectTopN(docs, keys, 10, descending: false);

        Assert.Equal(3, result.Length);
    }

    /// <summary>
    /// Verifies the Select Top N String: Returns Top N Ascending scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN String: Returns Top N Ascending")]
    public void SelectTopN_String_ReturnsTopN_Ascending()
    {
        var docs = MakeDocs(4);
        var keys = new string[] { "delta", "alpha", "charlie", "beta" };

        var result = TopNSortHelper.SelectTopN(docs, keys, 2, descending: false);

        Assert.Equal(2, result.Length);
        var resultDocs = result.Select(d => d.DocId).ToHashSet();
        Assert.Contains(1, resultDocs); // "alpha"
        Assert.Contains(3, resultDocs); // "beta"
    }

    /// <summary>
    /// Verifies the Select Top N String: Returns Top N Descending scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN String: Returns Top N Descending")]
    public void SelectTopN_String_ReturnsTopN_Descending()
    {
        var docs = MakeDocs(4);
        var keys = new string[] { "delta", "alpha", "charlie", "beta" };

        var result = TopNSortHelper.SelectTopN(docs, keys, 2, descending: true);

        Assert.Equal(2, result.Length);
        var resultDocs = result.Select(d => d.DocId).ToHashSet();
        Assert.Contains(0, resultDocs); // "delta"
        Assert.Contains(2, resultDocs); // "charlie"
    }

    /// <summary>
    /// Verifies the Select Top N String: Heap Path With Replacement scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN String: Heap Path With Replacement")]
    public void SelectTopN_String_HeapPathWithReplacement()
    {
        var docs = MakeDocs(6);
        var keys = new string[] { "cat", "ant", "fox", "bee", "gnu", "elk" };

        // topN=2 ascending: smallest 2 are "ant"(1) and "bee"(3)
        var result = TopNSortHelper.SelectTopN(docs, keys, 2, descending: false);

        Assert.Equal(2, result.Length);
        var resultDocs = result.Select(d => d.DocId).ToHashSet();
        Assert.Contains(1, resultDocs); // "ant"
        Assert.Contains(3, resultDocs); // "bee"
    }

    /// <summary>
    /// Verifies the Select Top N String: All Sorted Ascending When Top N Equals Count scenario.
    /// </summary>
    [Fact(DisplayName = "SelectTopN String: All Sorted Ascending When TopN Equals Count")]
    public void SelectTopN_String_AllSortedAscending_WhenTopNEqualsCount()
    {
        var docs = MakeDocs(3);
        var keys = new string[] { "cherry", "apple", "banana" };

        var result = TopNSortHelper.SelectTopN(docs, keys, 3, descending: false);

        Assert.Equal(3, result.Length);
        // sorted: apple(1), banana(2), cherry(0)
        Assert.Equal(1, result[0].DocId);
        Assert.Equal(2, result[1].DocId);
        Assert.Equal(0, result[2].DocId);
    }
}
