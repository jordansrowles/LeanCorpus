using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Unit tests for <see cref="InMemoryVectorSource"/> covering Count and GetVector branches.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class InMemoryVectorSourceTests
{
    private static InMemoryVectorSource Build(Dictionary<int, ReadOnlyMemory<float>> vectors, int dimension = 3)
        => new(vectors, dimension);

    [Fact(DisplayName = "InMemoryVectorSource.Count: Returns Number Of Stored Vectors")]
    public void Count_ReturnsNumberOfStoredVectors()
    {
        var source = Build(new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1f, 2f, 3f },
            [1] = new float[] { 4f, 5f, 6f },
        });
        Assert.Equal(2, source.Count);
    }

    [Fact(DisplayName = "InMemoryVectorSource.Count: Empty Dictionary Returns Zero")]
    public void Count_EmptyDictionary_ReturnsZero()
    {
        var source = Build(new Dictionary<int, ReadOnlyMemory<float>>());
        Assert.Equal(0, source.Count);
    }

    [Fact(DisplayName = "InMemoryVectorSource.GetVector: Known DocId Returns Correct Span")]
    public void GetVector_KnownDocId_ReturnsCorrectSpan()
    {
        float[] vec = [1f, 2f, 3f];
        var source = Build(new Dictionary<int, ReadOnlyMemory<float>> { [7] = vec });

        var span = source.GetVector(7);
        Assert.Equal(3, span.Length);
        Assert.Equal(1f, span[0]);
        Assert.Equal(2f, span[1]);
        Assert.Equal(3f, span[2]);
    }

    [Fact(DisplayName = "InMemoryVectorSource.GetVector: Missing DocId Throws KeyNotFoundException")]
    public void GetVector_MissingDocId_ThrowsKeyNotFoundException()
    {
        var source = Build(new Dictionary<int, ReadOnlyMemory<float>>());
        Assert.Throws<KeyNotFoundException>(() => source.GetVector(99));
    }

    [Fact(DisplayName = "InMemoryVectorSource: Null Vectors Dictionary Throws ArgumentNullException")]
    public void Constructor_NullVectors_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new InMemoryVectorSource(null!, 3));
}
