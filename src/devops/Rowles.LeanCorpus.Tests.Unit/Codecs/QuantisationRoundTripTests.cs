using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Round-trip tests for vector quantisation codecs.
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
            var deq = Assert.IsType<float[]>(reader.ReadVector(i));
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
        var deq = Assert.IsType<float[]>(reader.ReadVector(0));
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
        var deq = Assert.IsType<float[]>(reader.ReadVector(0));
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
            var deq = Assert.IsType<float[]>(reader.ReadVector(i));
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
        var deq0 = Assert.IsType<float[]>(reader.ReadVector(0));
        var deq1 = Assert.IsType<float[]>(reader.ReadVector(1));

        // Vector 0 is below centroid: all bits should be 0 → dequantised as centroid - 1
        for (int d = 0; d < dim; d++)
            Assert.True(deq0[d] < centroid[d], $"Expected below centroid, got {deq0[d]} vs {centroid[d]}");

        // Vector 1 is above centroid: all bits should be 1 → dequantised as centroid + 1
        for (int d = 0; d < dim; d++)
            Assert.True(deq1[d] > centroid[d], $"Expected above centroid, got {deq1[d]} vs {centroid[d]}");
    }

    [Fact(DisplayName = "Int4: Odd dimensions round-trip within half-bucket error")]
    public void Int4_OddDimensions_RoundTripWithinHalfBucket()
    {
        var original = CreateVectors(docCount: 17, dimension: 7, seed: 91);
        string path = TempFile("int4_odd.vq");

        QuantisedVectorWriter.WriteInt4(path, 17, 7, original);

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(VectorQuantisation.Int4, reader.Quantisation);
        for (int docId = 0; docId < 17; docId++)
        {
            float[] restored = Assert.IsType<float[]>(reader.ReadVector(docId));
            float error = L2Error(original[docId].Span, restored);
            Assert.True(error <= reader.GetInt4ErrorBound(docId) + 1e-5f);
            for (int dimension = 0; dimension < restored.Length; dimension++)
            {
                Assert.True(
                    MathF.Abs(original[docId].Span[dimension] - restored[dimension])
                    <= reader.Alpha * 0.51f);
            }
        }
    }

    [Fact(DisplayName = "Product quantisation: Trained codebooks round-trip deterministically")]
    public void ProductQuantisation_RoundTripsDeterministically()
    {
        var original = CreateVectors(docCount: 24, dimension: 11, seed: 103);
        string firstPath = TempFile("pq_first.vq");
        string secondPath = TempFile("pq_second.vq");

        QuantisedVectorWriter.WriteProductQuantisation(firstPath, 24, 11, original);
        QuantisedVectorWriter.WriteProductQuantisation(secondPath, 24, 11, original);

        Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));
        using var reader = QuantisedVectorReader.Open(firstPath);
        Assert.Equal(VectorQuantisation.ProductQuantisation, reader.Quantisation);
        for (int docId = 0; docId < 24; docId++)
        {
            float[] restored = Assert.IsType<float[]>(reader.ReadVector(docId));
            Assert.Equal(11, restored.Length);
            Assert.All(restored, value => Assert.True(float.IsFinite(value)));
        }
    }

    [Fact(DisplayName = "Product quantisation: Asymmetric lookup matches reconstructed dot product")]
    public void ProductQuantisation_AsymmetricLookupMatchesReconstruction()
    {
        var original = CreateVectors(docCount: 64, dimension: 16, seed: 107);
        string path = TempFile("pq_adc.vq");
        QuantisedVectorWriter.WriteProductQuantisation(path, 64, 16, original);

        using var reader = QuantisedVectorReader.Open(path);
        float[] query = original[7].ToArray();
        using ProductQuantisationQuery prepared = reader.PrepareProductQuery(
            query, VectorSimilarityFunction.DotProduct, normalised: true);
        for (int docId = 0; docId < 64; docId++)
        {
            float[] reconstructed = Assert.IsType<float[]>(reader.ReadVector(docId));
            float expected = -Dot(query, reconstructed);
            Assert.Equal(expected, prepared.DistanceTo(docId), precision: 5);
        }
    }

    [Fact(DisplayName = "Product quantisation: Production format separates routing and final codes")]
    public void ProductQuantisation_ProductionFormat_SeparatesRoutingAndFinalCodes()
    {
        const int docCount = 64;
        const int dimension = 16;
        var original = CreateVectors(docCount, dimension, seed: 109);
        string productionPath = TempFile("pq_two_level.vq");
        string routingReferencePath = TempFile("pq_routing_reference.vq");

        QuantisedVectorWriter.Write(
            productionPath,
            docCount,
            dimension,
            original,
            VectorQuantisation.ProductQuantisation);
        QuantisedVectorWriter.WriteProductQuantisation(
            routingReferencePath,
            docCount,
            dimension,
            original,
            subspaceDimensions: QuantisedVectorWriter.DefaultProductRoutingSubspaceDimensions);

        using var production = QuantisedVectorReader.Open(productionPath);
        using var routingReference = QuantisedVectorReader.Open(routingReferencePath);
        Assert.Equal(dimension, production.ProductSubquantiserCount);
        Assert.Equal(
            dimension / QuantisedVectorWriter.DefaultProductRoutingSubspaceDimensions,
            production.ProductRoutingSubquantiserCount);
        Assert.Equal(0, routingReference.ProductRoutingSubquantiserCount);
        Assert.Equal(
            routingReference.GetRawProductCodes(0).ToArray(),
            production.GetRawProductRoutingCodes(0).ToArray());

        float[] query = original[7].ToArray();
        using ProductQuantisationQuery productionQuery = production.PrepareProductQuery(
            query, VectorSimilarityFunction.DotProduct, normalised: true);
        using ProductQuantisationQuery referenceQuery = routingReference.PrepareProductQuery(
            query, VectorSimilarityFunction.DotProduct, normalised: true);
        for (int docId = 0; docId < docCount; docId++)
        {
            Assert.Equal(
                referenceQuery.DistanceTo(docId),
                productionQuery.DistanceTo(docId),
                precision: 5);
        }
    }

    [Fact(DisplayName = "RaBitQ: Random rotation round-trip honours persisted error bound")]
    public void RaBitQ_RoundTripHonoursErrorBound()
    {
        var original = CreateVectors(docCount: 13, dimension: 9, seed: 127);
        string path = TempFile("rabitq_odd.vq");

        QuantisedVectorWriter.WriteRaBitQ(path, 13, 9, original);

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(VectorQuantisation.RaBitQ, reader.Quantisation);
        for (int docId = 0; docId < 13; docId++)
        {
            float[] restored = Assert.IsType<float[]>(reader.ReadVector(docId));
            float actualError = L2Error(original[docId].Span, restored);
            Assert.True(
                actualError <= reader.GetRaBitQErrorBound(docId) + 1e-5f,
                $"Actual error {actualError} exceeded the persisted bound.");
        }
    }

    [Fact(DisplayName = "Product quantisation: Out-of-range persisted code is rejected")]
    public void ProductQuantisation_OutOfRangeCode_IsRejected()
    {
        var original = CreateVectors(docCount: 2, dimension: 3, seed: 149);
        string path = TempFile("pq_corrupt_code.vq");
        QuantisedVectorWriter.WriteProductQuantisation(path, 2, 3, original);

        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        {
            stream.Position = stream.Length - sizeof(long) - 1;
            stream.WriteByte(byte.MaxValue);
        }

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Throws<InvalidDataException>(() => reader.ReadVector(1));
    }

    [Fact(DisplayName = "RaBitQ: Invalid persisted rotation dimension is rejected")]
    public void RaBitQ_InvalidRotationDimension_IsRejected()
    {
        var original = CreateVectors(docCount: 1, dimension: 3, seed: 151);
        string path = TempFile("rabitq_corrupt_dimension.vq");
        QuantisedVectorWriter.WriteRaBitQ(path, 1, 3, original);

        const int rotationDimensionOffset =
            1 + sizeof(int) + sizeof(int) + sizeof(byte) + sizeof(int) + sizeof(int) + sizeof(long);
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Position = rotationDimensionOffset;
            writer.Write(3);
        }

        Assert.Throws<InvalidDataException>(() => QuantisedVectorReader.Open(path));
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

    [Theory(DisplayName = "Quantised vectors: Missing documents do not manufacture vectors")]
    [InlineData(VectorQuantisation.Int8)]
    [InlineData(VectorQuantisation.BBQ)]
    [InlineData(VectorQuantisation.Int4)]
    [InlineData(VectorQuantisation.ProductQuantisation)]
    [InlineData(VectorQuantisation.RaBitQ)]
    public void QuantisedVectors_MissingDocuments_DoNotManufactureVectors(
        VectorQuantisation quantisation)
    {
        const int docCount = 4;
        const int dimension = 3;
        var original = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [1] = new float[] { -1f, 0f, 1f },
            [3] = new float[] { 1f, 0f, -1f },
        };
        string path = TempFile($"sparse_{quantisation}.vq");

        QuantisedVectorWriter.Write(
            path,
            docCount,
            dimension,
            original,
            quantisation,
            new float[dimension]);

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(docCount, reader.DocCount);
        Assert.Equal(2, reader.VectorCount);
        Assert.False(reader.HasVector(0));
        Assert.True(reader.HasVector(1));
        Assert.False(reader.HasVector(2));
        Assert.True(reader.HasVector(3));
        Assert.Null(reader.ReadVector(0));
        Assert.Null(reader.ReadVector(2));
        Assert.NotNull(reader.ReadVector(1));
        Assert.NotNull(reader.ReadVector(3));
        Assert.Throws<KeyNotFoundException>(() => reader.ReadVector(0, new float[dimension]));
    }

    [Fact(DisplayName = "Float vectors: Missing documents do not manufacture vectors")]
    public void FloatVectors_MissingDocuments_DoNotManufactureVectors()
    {
        const int docCount = 4;
        const int dimension = 2;
        var original = new Dictionary<int, ReadOnlyMemory<float>>
        {
            [1] = new float[] { -1f, 0f },
            [3] = new float[] { 1f, 0f },
        };
        string path = TempFile("sparse_float.vec");

        VectorWriter.WriteField(path, docCount, dimension, original);

        using var reader = VectorReader.Open(path);
        Assert.Equal(docCount, reader.DocCount);
        Assert.Equal(2, reader.VectorCount);
        Assert.False(reader.HasVector(0));
        Assert.True(reader.HasVector(1));
        Assert.False(reader.HasVector(2));
        Assert.True(reader.HasVector(3));
        Assert.Null(reader.ReadVector(0));
        Assert.Equal([-1f, 0f], Assert.IsType<float[]>(reader.ReadVector(1)));
    }

    [Fact(DisplayName = "Float vectors: mapped block exposes the persisted values without reconstruction")]
    public void FloatVectors_MappedBlock_ExposesPersistedValues()
    {
        string path = TempFile("mapped_float.vec");
        VectorWriter.WriteField(path, docCount: 2, dimension: 3,
            new Dictionary<int, ReadOnlyMemory<float>>
            {
                [0] = new float[] { 1.5f, -2f, 3.25f },
                [1] = new float[] { 4f, 5f, 6f },
            });

        using var reader = VectorReader.Open(path);
        var source = new VectorReaderSource(reader);
        ReadOnlySpan<float> vector = source.GetVector(0);

        Assert.Equal(new float[] { 1.5f, -2f, 3.25f }, vector.ToArray());
        _ = source.GetVector(0)[0]; // warm JIT and mapped-reader path
        long before = GC.GetAllocatedBytesForCurrentThread();
        float sum = 0f;
        for (int i = 0; i < 1_000; i++)
            sum += source.GetVector(0)[i % 3];
        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.NotEqual(0f, sum);
        Assert.Equal(before, after);
        Assert.Throws<ArgumentOutOfRangeException>(() => { source.GetVector(7); });
    }

    [Fact(DisplayName = "Compatibility: Version 1 float vectors remain readable")]
    public void Compatibility_Version1FloatVectors_RemainReadable()
    {
        string path = TempFile("legacy_v1.vec");
        using (var output = new IndexOutput(path))
        using (CodecFileHeader.BeginStreamingWrite(output, version: 1))
        {
            output.WriteInt32(2);
            output.WriteInt32(2);
            output.WriteByte((byte)VectorQuantisation.None);
            output.WriteSingle(1f);
            output.WriteSingle(2f);
            output.WriteSingle(3f);
            output.WriteSingle(4f);
        }

        using var reader = VectorReader.Open(path);
        Assert.Equal(2, reader.DocCount);
        Assert.Equal(2, reader.VectorCount);
        Assert.Equal([1f, 2f], Assert.IsType<float[]>(reader.ReadVector(0)));
        Assert.Equal([3f, 4f], Assert.IsType<float[]>(reader.ReadVector(1)));
    }

    [Fact(DisplayName = "Compatibility: Version 1 quantised vectors remain readable")]
    public void Compatibility_Version1QuantisedVectors_RemainReadable()
    {
        string path = TempFile("legacy_v1.vq");
        using (var output = new IndexOutput(path))
        using (CodecFileHeader.BeginStreamingWrite(output, version: 1))
        {
            output.WriteInt32(2);
            output.WriteInt32(1);
            output.WriteByte((byte)VectorQuantisation.Int8);
            output.WriteSingle(0f);
            output.WriteSingle(1f);
            output.WriteSingle(0f);
            output.WriteSingle(0f);
            output.WriteByte(1);
            output.WriteByte(2);
        }

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(2, reader.DocCount);
        Assert.Equal(2, reader.VectorCount);
        Assert.Equal([1f], Assert.IsType<float[]>(reader.ReadVector(0)));
        Assert.Equal([2f], Assert.IsType<float[]>(reader.ReadVector(1)));
    }

    [Fact(DisplayName = "Compatibility: Version 4 single-level product vectors remain readable")]
    public void Compatibility_Version4ProductVectors_RemainReadable()
    {
        string path = TempFile("legacy_v4_pq.vq");
        using (var output = new IndexOutput(path))
        using (CodecFileHeader.BeginStreamingWrite(output, version: 4))
        {
            output.WriteInt32(1);
            output.WriteInt32(1);
            output.WriteByte((byte)VectorQuantisation.ProductQuantisation);
            output.WriteInt32(1);
            output.WriteInt32(0);
            output.WriteInt32(1);
            output.WriteInt32(1);
            output.WriteInt32(0);
            output.WriteInt32(1);
            output.WriteSingle(2.5f);
            output.WriteByte(0);
        }

        using var reader = QuantisedVectorReader.Open(path);
        Assert.Equal(0, reader.ProductRoutingSubquantiserCount);
        Assert.Equal([2.5f], Assert.IsType<float[]>(reader.ReadVector(0)));
    }

    private static Dictionary<int, ReadOnlyMemory<float>> CreateVectors(
        int docCount,
        int dimension,
        int seed)
    {
        var random = new Random(seed);
        var vectors = new Dictionary<int, ReadOnlyMemory<float>>();
        for (int docId = 0; docId < docCount; docId++)
        {
            var vector = new float[dimension];
            for (int i = 0; i < dimension; i++)
                vector[i] = (float)(random.NextDouble() * 4d - 2d);
            vectors[docId] = vector;
        }
        return vectors;
    }

    private static float L2Error(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual)
    {
        float squaredError = 0f;
        for (int i = 0; i < expected.Length; i++)
        {
            float error = expected[i] - actual[i];
            squaredError += error * error;
        }
        return MathF.Sqrt(squaredError);
    }

    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        float sum = 0f;
        for (int i = 0; i < left.Length; i++)
            sum += left[i] * right[i];
        return sum;
    }
}
