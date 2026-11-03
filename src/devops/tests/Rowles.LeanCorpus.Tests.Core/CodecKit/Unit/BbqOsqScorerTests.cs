using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class BbqOsqScorerTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(384)]
    [InlineData(768)]
    [InlineData(1536)]
    public void PackedAndScalarKernels_AgreeWithIndependentDecodedDot(int dimension)
    {
        float[] centroid = new float[dimension];
        float[] queryVector = new float[dimension];
        var random = new Random(701 + dimension);
        for (int index = 0; index < dimension; index++)
        {
            queryVector[index] = (float)(random.NextDouble() * 2d - 1d);
            centroid[index] = (float)(random.NextDouble() * 0.02d - 0.01d);
        }
        Normalise(queryVector);
        Normalise(centroid);
        PreparedBbqQuery query = PreparedBbqQuery.Prepare(queryVector, centroid);

        byte[] documentCodes = new byte[dimension];
        for (int index = 0; index < dimension; index++)
            documentCodes[index] = (byte)((index * 7 + dimension) % 2);
        byte[] packedDocument = new byte[(dimension + 7) / 8];
        OptimisedScalarQuantiser.PackBinary(documentCodes, packedDocument);
        int documentCodeSum = documentCodes.Sum(static code => (int)code);
        var documentCorrections = new BbqOsqCorrections(-0.125f, 0.375f, 0.0625f, documentCodeSum);
        float centroidSquaredNorm = Dot(centroid, centroid);

        float scalar = BbqOsqDistanceComputer.ScoreScalar(query, packedDocument, documentCorrections, centroidSquaredNorm);
        float packed = BbqOsqDistanceComputer.ScorePacked(query, packedDocument, documentCorrections, centroidSquaredNorm);
        float independent = IndependentDecodedCosineEstimate(query, documentCodes, documentCorrections, centroidSquaredNorm);

        Assert.Equal(BitConverter.SingleToInt32Bits(scalar), BitConverter.SingleToInt32Bits(packed));
        Assert.InRange(MathF.Abs(independent - scalar), 0f, 1e-6f);
        Assert.InRange(scalar, -1f, 1f);
    }

    [Fact]
    public void ScorePacked_DoesNotAllocatePerCandidate()
    {
        const int dimension = 384;
        float[] queryVector = Enumerable.Range(0, dimension)
            .Select(static index => (float)Math.Sin(index * 0.17d))
            .ToArray();
        float[] centroid = Enumerable.Range(0, dimension)
            .Select(static index => (float)Math.Cos(index * 0.11d) * 0.01f)
            .ToArray();
        Normalise(queryVector);
        Normalise(centroid);
        PreparedBbqQuery query = PreparedBbqQuery.Prepare(queryVector, centroid);
        byte[] documentCodes = Enumerable.Range(0, dimension)
            .Select(static index => (byte)(index % 2))
            .ToArray();
        byte[] packedDocument = new byte[(dimension + 7) / 8];
        OptimisedScalarQuantiser.PackBinary(documentCodes, packedDocument);
        var corrections = new BbqOsqCorrections(-0.2f, 0.4f, 0.1f, documentCodes.Sum(static code => (int)code));

        for (int index = 0; index < 4; index++)
            _ = BbqOsqDistanceComputer.ScorePacked(query, packedDocument, corrections, Dot(centroid, centroid));
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
            _ = BbqOsqDistanceComputer.ScorePacked(query, packedDocument, corrections, Dot(centroid, centroid));

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Prepare_DoesNotMutateTheInputQuery()
    {
        float[] query = Enumerable.Range(0, 128)
            .Select(static index => (float)Math.Sin(index * 0.1d))
            .ToArray();
        float[] centroid = Enumerable.Range(0, 128)
            .Select(static index => (float)Math.Cos(index * 0.2d) * 0.01f)
            .ToArray();
        Normalise(query);
        Normalise(centroid);
        float[] original = (float[])query.Clone();

        _ = PreparedBbqQuery.Prepare(query, centroid);

        Assert.Equal(original, query);
    }

    private static float IndependentDecodedCosineEstimate(
        PreparedBbqQuery query,
        ReadOnlySpan<byte> documentCodes,
        BbqOsqCorrections documentCorrections,
        float centroidSquaredNorm)
    {
        ReadOnlySpan<byte> queryCodes = query.QueryCodes;
        float documentStep = documentCorrections.UpperInterval - documentCorrections.LowerInterval;
        float queryStep = (query.Corrections.UpperInterval - query.Corrections.LowerInterval) / 15f;
        double dot = 0d;
        for (int index = 0; index < documentCodes.Length; index++)
        {
            double documentValue = documentCorrections.LowerInterval + documentStep * documentCodes[index];
            double queryValue = query.Corrections.LowerInterval + queryStep * queryCodes[index];
            dot += documentValue * queryValue;
        }
        dot += query.Corrections.AdditionalCorrection + documentCorrections.AdditionalCorrection - centroidSquaredNorm;
        return Math.Clamp((float)dot, -1f, 1f);
    }

    private static void Normalise(Span<float> vector)
    {
        double norm = 0d;
        foreach (float value in vector)
            norm += (double)value * value;
        float inverse = (float)(1d / Math.Sqrt(norm));
        for (int index = 0; index < vector.Length; index++)
            vector[index] *= inverse;
    }

    private static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        float dot = 0f;
        for (int index = 0; index < left.Length; index++)
            dot += left[index] * right[index];
        return dot;
    }
}
