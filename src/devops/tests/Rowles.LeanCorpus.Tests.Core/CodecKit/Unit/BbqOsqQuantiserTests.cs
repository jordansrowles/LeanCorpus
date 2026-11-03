using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class BbqOsqQuantiserTests
{
    [Fact]
    public void Quantize_MatchesLucene1052SingleBitAndNibbleGoldensWithoutMutatingInput()
    {
        float[] vector = [-0.6f, -0.4f, -0.2f, 0f, 0.1f, 0.2f, 0.5f, 0.8f];
        float[] centroid = [0.1f, -0.05f, 0.025f, 0f, -0.1f, 0.05f, 0.075f, -0.025f];
        float[] original = (float[])vector.Clone();
        Span<float> residuals = stackalloc float[vector.Length];
        Span<byte> documentCodes = stackalloc byte[vector.Length];
        Span<byte> queryCodes = stackalloc byte[vector.Length];

        BbqOsqCorrections document = OptimisedScalarQuantiser.Quantise(vector, centroid, 1, residuals, documentCodes);
        BbqOsqCorrections query = OptimisedScalarQuantiser.Quantise(vector, centroid, 4, residuals, queryCodes);

        Assert.Equal(new byte[] { 0, 0, 0, 0, 1, 1, 1, 1 }, documentCodes.ToArray());
        Assert.Equal(-0.45964378f, document.LowerInterval);
        Assert.Equal(0.57680786f, document.UpperInterval);
        Assert.Equal(-0.027499998f, document.AdditionalCorrection);
        Assert.Equal(4, document.CodeSum);

        Assert.Equal(new byte[] { 0, 3, 5, 7, 9, 8, 11, 15 }, queryCodes.ToArray());
        Assert.Equal(-0.6942102f, query.LowerInterval);
        Assert.Equal(0.82636476f, query.UpperInterval);
        Assert.Equal(-0.027499998f, query.AdditionalCorrection);
        Assert.Equal(58, query.CodeSum);
        Assert.Equal(original, vector);
    }

    [Fact]
    public void Quantize_HandlesAZeroWidthResidualInterval()
    {
        float[] vector = [0.25f, -0.5f, 0.125f, 0.75f];
        float[] centroid = (float[])vector.Clone();
        Span<float> residuals = stackalloc float[vector.Length];
        Span<byte> codes = stackalloc byte[vector.Length];

        BbqOsqCorrections corrections = OptimisedScalarQuantiser.Quantise(vector, centroid, 4, residuals, codes);

        Assert.Equal(0f, corrections.LowerInterval);
        Assert.Equal(0f, corrections.UpperInterval);
        Assert.Equal(0.890625f, corrections.AdditionalCorrection);
        Assert.Equal(0, corrections.CodeSum);
        Assert.All(codes.ToArray(), static code => Assert.Equal((byte)0, code));
    }

    [Theory]
    [InlineData(2.5f, 3)]
    [InlineData(-2.5f, -2)]
    [InlineData(0.5f, 1)]
    [InlineData(-0.5f, 0)]
    public void RoundLikeJava_UsesTiesTowardsPositiveInfinity(float value, int expected)
        => Assert.Equal(expected, OptimisedScalarQuantiser.RoundLikeJava(value));

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
    public void PackBinary_UsesLuceneBitOrderAndZeroesPadding(int dimension)
    {
        byte[] source = Enumerable.Range(0, dimension)
            .Select(static index => (byte)(index & 1))
            .ToArray();
        byte[] packed = new byte[(dimension + 7) / 8];

        OptimisedScalarQuantiser.PackBinary(source, packed);

        for (int index = 0; index < dimension; index++)
        {
            int bit = (packed[index >> 3] >> (7 - (index & 7))) & 1;
            Assert.Equal(source[index], bit);
        }

        int usedBits = dimension & 7;
        if (usedBits != 0)
            Assert.Equal(0, packed[^1] & ((1 << (8 - usedBits)) - 1));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Quantize_RejectsNonFiniteInput(float value)
    {
        Assert.Throws<ArgumentException>(() => QuantizeNonFinite(value));
    }

    private static void QuantizeNonFinite(float value)
    {
        float[] vector = [value, 0f];
        float[] centroid = [0f, 0f];
        Span<float> residuals = stackalloc float[2];
        Span<byte> codes = stackalloc byte[2];
        _ = OptimisedScalarQuantiser.Quantise(vector, centroid, 1, residuals, codes);
    }
}
