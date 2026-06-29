namespace Rowles.LeanCorpus.Tests.Unit.Search.Scoring;

/// <summary>
/// Unit tests for <see cref="ScoreDoc"/> record struct.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class ScoreDocTests
{
    [Fact(DisplayName = "ScoreDoc: Positional constructor sets DocId and Score")]
    public void Constructor_SetsDocIdAndScore()
    {
        var doc = new ScoreDoc(42, 1.5f);
        Assert.Equal(42, doc.DocId);
        Assert.Equal(1.5f, doc.Score);
    }

    [Fact(DisplayName = "ScoreDoc: EstimatedBytes equals 12")]
    public void EstimatedBytes_Is12()
    {
        Assert.Equal(12, ScoreDoc.EstimatedBytes);
    }

    [Fact(DisplayName = "ScoreDoc: Equal instances are equal")]
    public void EqualInstances_AreEqual()
    {
        var a = new ScoreDoc(1, 2.5f);
        var b = new ScoreDoc(1, 2.5f);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "ScoreDoc: Different DocId are not equal")]
    public void DifferentDocId_NotEqual()
    {
        var a = new ScoreDoc(1, 2.5f);
        var b = new ScoreDoc(2, 2.5f);
        Assert.NotEqual(a, b);
        Assert.True(a != b);
    }

    [Fact(DisplayName = "ScoreDoc: Different Score are not equal")]
    public void DifferentScore_NotEqual()
    {
        var a = new ScoreDoc(1, 2.5f);
        var b = new ScoreDoc(1, 3.0f);
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "ScoreDoc: Zero values round-trip")]
    public void ZeroValues_RoundTrip()
    {
        var doc = new ScoreDoc(0, 0f);
        Assert.Equal(0, doc.DocId);
        Assert.Equal(0f, doc.Score);
    }

    [Fact(DisplayName = "ScoreDoc: Negative DocId round-trips")]
    public void NegativeDocId_RoundTrips()
    {
        var doc = new ScoreDoc(-1, 0.5f);
        Assert.Equal(-1, doc.DocId);
    }

    [Fact(DisplayName = "ScoreDoc: Deconstruction produces expected values")]
    public void Deconstruction_ProducesExpectedValues()
    {
        var doc = new ScoreDoc(7, 3.14f);
        (int id, float score) = doc;
        Assert.Equal(7, id);
        Assert.Equal(3.14f, score);
    }

    [Fact(DisplayName = "ScoreDoc: ToString includes DocId and Score")]
    public void ToString_IncludesBothValues()
    {
        var doc = new ScoreDoc(5, 0.99f);
        var s = doc.ToString();
        Assert.Contains("5", s);
        Assert.Contains("0.99", s);
    }
}
