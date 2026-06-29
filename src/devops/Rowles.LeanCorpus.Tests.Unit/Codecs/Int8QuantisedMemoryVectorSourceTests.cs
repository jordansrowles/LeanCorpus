using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Unit tests for <see cref="Int8QuantisedMemoryVectorSource"/> covering round-trip dequantisation,
/// dimension/count correctness, and edge cases.
/// </summary>
[Trait("Category", "Codecs")]
[Trait("Category", "UnitTest")]
public sealed class Int8QuantisedMemoryVectorSourceTests
{
    [Fact(DisplayName = "Int8MemorySource: Round-trip within half-bucket error")]
    public void RoundTrip_WithinHalfBucket()
    {
        const int n = 50;
        const int dim = 16;
        var rng = new Random(123);
        var dict = new Dictionary<int, ReadOnlyMemory<float>>();
        for (int i = 0; i < n; i++)
        {
            var v = new float[dim];
            for (int d = 0; d < dim; d++)
                v[d] = (float)(rng.NextDouble() * 2 - 1);
            dict[i] = v;
        }

        float min = float.MaxValue, max = float.MinValue;
        foreach (var v in dict.Values)
            foreach (float val in v.Span)
            { if (val < min) min = val; if (val > max) max = val; }
        if (MathF.Abs(max - min) < 1e-8f) max = min + 1f;
        float alpha = (max - min) / 255f;

        var src = new Int8QuantisedMemoryVectorSource(dict, dim, min, alpha);

        Assert.Equal(n, src.Count);
        Assert.Equal(dim, src.Dimension);

        float maxErr = 0f;
        for (int i = 0; i < n; i++)
        {
            var deq = src.GetVector(i).ToArray();
            var orig = dict[i].Span;
            for (int d = 0; d < dim; d++)
                maxErr = MathF.Max(maxErr, MathF.Abs(deq[d] - orig[d]));
        }
        Assert.True(maxErr <= alpha * 0.55f, $"Max error {maxErr:E4} exceeds alpha/2 ({alpha / 2f:E4})");
    }

    [Fact(DisplayName = "Int8MemorySource: Missing docId throws")]
    public void MissingDocId_Throws()
    {
        var dict = new Dictionary<int, ReadOnlyMemory<float>> { [0] = new float[] { 1f, 2f } };
        var src = new Int8QuantisedMemoryVectorSource(dict, 2, 0f, 0.1f);
        Assert.Throws<ArgumentOutOfRangeException>(() => src.GetVector(99));
    }

    [Fact(DisplayName = "Int8MemorySource: Single vector round-trips")]
    public void SingleVector_RoundTrips()
    {
        var dict = new Dictionary<int, ReadOnlyMemory<float>> { [0] = new float[] { 3f, 7f } };
        var src = new Int8QuantisedMemoryVectorSource(dict, 2, 3f, (7f - 3f) / 255f);
        var deq = src.GetVector(0).ToArray();
        Assert.Equal(3f, deq[0], 0.1f);
        Assert.Equal(7f, deq[1], 0.1f);
    }
}
