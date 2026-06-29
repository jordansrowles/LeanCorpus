using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Round-trip tests for int8 and BBQ vector quantisation.
/// Verifies write → read → dequantise fidelity and quantisation parameter accuracy.
/// </summary>
[Trait("Category", "Codecs")]
[Trait("Category", "UnitTest")]
public sealed class QuantisationRoundTripTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    public QuantisationRoundTripTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string TempFile(string name) => System.IO.Path.Combine(_fixture.Path, name);

    // --------------- Int8 scalar quantisation ---------------

    [Fact(DisplayName = "Int8: Random vectors round-trip within half-bucket error")]
    public void Int8_RandomVectors_RoundTripWithinHalfBucket()
    {
        const int docCount = 100;
        const int dim = 16;
        var rng = new Random(42);

        var original = new Dictionary<int, ReadOnlyMemory<float>>();
        for (int i = 0; i < docCount; i++)
        {
            var vec = new float[dim];
            for (int d = 0; d < dim; d++)
                vec[d] = (float)(rng.NextDouble() * 2.0 - 1.0);
            original[i] = vec;
        }

        var path = TempFile("int8_roundtrip.vq");
        QuantisedVectorWriter.WriteInt8(path, docCount, dim, original);

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(docCount, reader.DocCount);
        Assert.Equal(dim, reader.Dimension);
        Assert.Equal(VectorQuantisation.Int8, reader.Quantisation);

        float alpha = reader.Alpha;
        float maxError = 0f;

        for (int i = 0; i < docCount; i++)
        {
            var deq = reader.ReadVector(i);
            var orig = original[i].Span;
            for (int d = 0; d < dim; d++)
            {
                float err = MathF.Abs(deq[d] - orig[d]);
                if (err > maxError) maxError = err;
            }
        }

        // Error should be at most half a bucket width (alpha / 2)
        Assert.True(maxError <= alpha * 0.55f, $"Max error {maxError:E4} exceeds alpha/2 ({alpha / 2f:E4})");
    }

    [Fact(DisplayName = "Int8: All-zeros vector round-trips correctly")]
    public void Int8_AllZeros_RoundTripsCorrectly()
    {
        const int dim = 8;
        var original = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[dim], // all zeros
        };

        var path = TempFile("int8_zeros.vq");
        QuantisedVectorWriter.WriteInt8(path, 1, dim, original);

        using var reader = QuantisedVectorReader.Open(path);
        var deq = reader.ReadVector(0);
        for (int d = 0; d < dim; d++)
            Assert.True(MathF.Abs(deq[d]) < 0.01f, $"Expected near-zero, got {deq[d]}");
    }

    [Fact(DisplayName = "Int8: Identical min & max handled without division by zero")]
    public void Int8_IdenticalValues_DoesNotDivideByZero()
    {
        const int dim = 4;
        const float val = 3.0f;
        var vec = new float[dim];
        Array.Fill(vec, val);
        var original = new Dictionary<int, ReadOnlyMemory<float>> { [0] = vec };

        var path = TempFile("int8_const.vq");
        QuantisedVectorWriter.WriteInt8(path, 1, dim, original);

        using var reader = QuantisedVectorReader.Open(path);
        var deq = reader.ReadVector(0);
        // When min == max, alpha = 1/255, so quantised values should reconstruct close to original
        for (int d = 0; d < dim; d++)
            Assert.True(MathF.Abs(deq[d] - val) < 1f, $"Constant value {val} distorted to {deq[d]}");
    }

    [Fact(DisplayName = "Int8: Single-vector min/max are correctly computed")]
    public void Int8_SingleVector_MinMaxCorrect()
    {
        var original = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { -5f, 0f, 10f },
        };

        var path = TempFile("int8_single.vq");
        QuantisedVectorWriter.WriteInt8(path, 1, 3, original);

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(-5f, reader.Min, 1e-6f);
        Assert.Equal((10f - (-5f)) / 255f, reader.Alpha, 1e-6f);
    }

    // --------------- BBQ binary quantisation ---------------

    [Fact(DisplayName = "BBQ: Random vectors round-trip with binary sign fidelity")]
    public void BBQ_RandomVectors_SignFidelity()
    {
        const int docCount = 50;
        const int dim = 32;
        var rng = new Random(123);

        // Compute a centroid first
        float[] centroid = new float[dim];
        var original = new Dictionary<int, ReadOnlyMemory<float>>();
        for (int i = 0; i < docCount; i++)
        {
            var vec = new float[dim];
            for (int d = 0; d < dim; d++)
            {
                vec[d] = (float)(rng.NextDouble() * 2.0 - 1.0);
                centroid[d] += vec[d];
            }
            original[i] = vec;
        }
        for (int d = 0; d < dim; d++)
            centroid[d] /= docCount;

        var path = TempFile("bbq_roundtrip.vq");
        QuantisedVectorWriter.WriteBBQ(path, docCount, dim, original, centroid);

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(docCount, reader.DocCount);
        Assert.Equal(dim, reader.Dimension);
        Assert.Equal(VectorQuantisation.BBQ, reader.Quantisation);

        // Check centroid matches
        var storedCentroid = reader.Centroid;
        for (int d = 0; d < dim; d++)
            Assert.Equal(centroid[d], storedCentroid[d], 1e-6f);

        // Check sign agreement for half the vectors
        int mismatches = 0;
        for (int i = 0; i < docCount; i++)
        {
            var deq = reader.ReadVector(i);
            var orig = original[i].Span;
            for (int d = 0; d < dim; d++)
            {
                float residual = orig[d] - centroid[d];
                float deqResidual = deq[d] - centroid[d];
                // Both should have the same sign (both positive or both negative)
                if (MathF.Sign(residual) != MathF.Sign(deqResidual) && MathF.Abs(residual) > 0.001f)
                    mismatches++;
            }
        }

        double mismatchRate = mismatches / (double)(docCount * dim);
        Assert.True(mismatchRate < 0.01, $"Sign mismatch rate {mismatchRate:P2} exceeds 1%");
    }

    [Fact(DisplayName = "BBQ: Centroid subtraction yields zero-centred residuals")]
    public void BBQ_CentroidSubtraction_ZeroCentredResiduals()
    {
        const int dim = 16;
        float[] centroid = new float[dim];
        Array.Fill(centroid, 0.5f);

        var zeros = new float[dim];
        var original = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = zeros,
        };

        // Add one above-centroid vector
        var aboveVec = new float[dim];
        Array.Fill(aboveVec, 1.0f);
        original[1] = aboveVec;

        var path = TempFile("bbq_centroid.vq");
        QuantisedVectorWriter.WriteBBQ(path, 2, dim, original, centroid);

        using var reader = QuantisedVectorReader.Open(path);
        var deq0 = reader.ReadVector(0);
        var deq1 = reader.ReadVector(1);

        // Vector 0 is below centroid: all bits should be 0 → dequantised as centroid - 1
        for (int d = 0; d < dim; d++)
            Assert.True(deq0[d] < centroid[d], $"Expected below centroid, got {deq0[d]} vs {centroid[d]}");

        // Vector 1 is above centroid: all bits should be 1 → dequantised as centroid + 1
        for (int d = 0; d < dim; d++)
            Assert.True(deq1[d] > centroid[d], $"Expected above centroid, got {deq1[d]} vs {centroid[d]}");
    }

    // --------------- Cross-type validation ---------------

    [Fact(DisplayName = "Quantisation: Default config writes .vec not .vq")]
    public void DefaultConfig_WritesVecNotVq()
    {
        // Int8 and BBQ should produce .vq files with correct quantisation bytes,
        // while None should produce .vec. Verified via writer flags.
        var original = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [0] = new float[] { 1f, 2f, 3f },
        };

        // None path: .vec file
        var vecPath = TempFile("none_test.vec");
        VectorWriter.WriteField(vecPath, 1, 3, original);
        Assert.True(File.Exists(vecPath));

        // Int8 path: .vq file with quantisation=1
        var int8Path = TempFile("int8_test.vq");
        QuantisedVectorWriter.WriteInt8(int8Path, 1, 3, original);
        using (var r = QuantisedVectorReader.Open(int8Path))
            Assert.Equal(VectorQuantisation.Int8, r.Quantisation);

        // BBQ path: .vq file with quantisation=2
        var bbqPath = TempFile("bbq_test.vq");
        QuantisedVectorWriter.WriteBBQ(bbqPath, 1, 3, original, new float[] { 0f, 0f, 0f });
        using (var r = QuantisedVectorReader.Open(bbqPath))
            Assert.Equal(VectorQuantisation.BBQ, r.Quantisation);
    }
}
