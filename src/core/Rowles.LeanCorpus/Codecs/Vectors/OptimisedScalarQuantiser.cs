namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>The four correction values stored for one OSQ vector.</summary>
internal readonly record struct BbqOsqCorrections(
    float LowerInterval,
    float UpperInterval,
    float AdditionalCorrection,
    int CodeSum);

/// <summary>
/// Scalar implementation of Apache Lucene 10.5.2 OptimizedScalarQuantizer interval
/// selection for cosine vectors. The port keeps Java's midpoint rule and does not
/// modify the caller's source vector. Upstream source is pinned in the BBQ v3
/// implementation reference package under Apache-2.0.
/// </summary>
internal static class OptimisedScalarQuantiser
{
    private const float Lambda = 0.1f;
    private const int OptimisationIterations = 5;

    private static ReadOnlySpan<float> MinimumMseGrid =>
    [
        -0.798f, 0.798f,
        -1.493f, 1.493f,
        -2.051f, 2.051f,
        -2.514f, 2.514f,
        -2.916f, 2.916f,
        -3.278f, 3.278f,
        -3.611f, 3.611f,
        -3.922f, 3.922f
    ];

    /// <summary>
    /// Quantises a vector relative to a unit cosine centroid. <paramref name="residualScratch"/>
    /// is caller-owned and receives the centred vector; source and centroid remain unchanged.
    /// </summary>
    internal static BbqOsqCorrections Quantise(
        ReadOnlySpan<float> vector,
        ReadOnlySpan<float> centroid,
        int bits,
        Span<float> residualScratch,
        Span<byte> codes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(vector.Length);
        if (centroid.Length != vector.Length)
            throw new ArgumentException("Centroid length must match the vector dimension.", nameof(centroid));
        if (residualScratch.Length != vector.Length)
            throw new ArgumentException("Residual scratch length must match the vector dimension.", nameof(residualScratch));
        if (codes.Length != vector.Length)
            throw new ArgumentException("Code destination length must match the vector dimension.", nameof(codes));
        if (bits is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(bits), "OSQ supports between one and eight bits.");

        int points = 1 << bits;
        int maximumCode = points - 1;
        float minimum = float.MaxValue;
        float maximum = float.MinValue;
        float normSquared = 0f;
        float centroidDot = 0f;
        double mean = 0d;
        double varianceAccumulator = 0d;

        for (int index = 0; index < vector.Length; index++)
        {
            float value = vector[index];
            float centre = centroid[index];
            if (!float.IsFinite(value) || !float.IsFinite(centre))
                throw new ArgumentException("OSQ vectors and centroids must contain only finite values.", nameof(vector));

            centroidDot += value * centre;
            float residual = value - centre;
            if (!float.IsFinite(residual))
                throw new ArgumentException("OSQ residual is outside the supported finite range.", nameof(vector));
            residualScratch[index] = residual;
            minimum = MathF.Min(minimum, residual);
            maximum = MathF.Max(maximum, residual);
            normSquared += residual * residual;
            double delta = residual - mean;
            mean += delta / (index + 1);
            varianceAccumulator += delta * (residual - mean);
        }

        if (!float.IsFinite(normSquared) || !float.IsFinite(centroidDot))
            throw new ArgumentException("OSQ vector norm or correction is outside the supported finite range.", nameof(vector));

        double standardDeviation = Math.Sqrt(varianceAccumulator / vector.Length);
        float lower = Clamp((float)(MinimumMseGrid[(bits - 1) * 2] * standardDeviation + mean), minimum, maximum);
        float upper = Clamp((float)(MinimumMseGrid[(bits - 1) * 2 + 1] * standardDeviation + mean), minimum, maximum);

        if (minimum != maximum && normSquared > 0f)
            OptimiseIntervals(residualScratch, normSquared, points, ref lower, ref upper);

        if (!float.IsFinite(lower) || !float.IsFinite(upper) || lower > upper)
            throw new ArgumentException("OSQ produced an invalid correction interval.", nameof(vector));

        int codeSum = 0;
        if (lower == upper)
        {
            codes.Clear();
        }
        else
        {
            float step = (upper - lower) / maximumCode;
            if (!float.IsFinite(step) || step <= 0f)
                throw new ArgumentException("OSQ interval step is outside the supported finite range.", nameof(vector));

            for (int index = 0; index < vector.Length; index++)
            {
                float clamped = Clamp(residualScratch[index], lower, upper);
                int assignment = Math.Clamp(RoundLikeJava((clamped - lower) / step), 0, maximumCode);
                codes[index] = (byte)assignment;
                codeSum = checked(codeSum + assignment);
            }
        }

        return new BbqOsqCorrections(lower, upper, centroidDot, codeSum);
    }

    /// <summary>Matches Java Math.round(float): halfway values move towards positive infinity.</summary>
    internal static int RoundLikeJava(float value)
    {
        if (!float.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "OSQ rounding requires a finite value.");
        return checked((int)MathF.Floor(value + 0.5f));
    }

    /// <summary>Packs one-bit codes MSB-first, matching Lucene's SINGLE_BIT_QUERY_NIBBLE encoding.</summary>
    internal static void PackBinary(ReadOnlySpan<byte> codes, Span<byte> packed)
    {
        int packedLength = checked((codes.Length + 7) / 8);
        if (packed.Length != packedLength)
            throw new ArgumentException($"Packed destination length {packed.Length} != required length {packedLength}.", nameof(packed));

        packed.Clear();
        for (int index = 0; index < codes.Length; index++)
        {
            byte code = codes[index];
            if (code > 1)
                throw new ArgumentException("One-bit OSQ codes must be zero or one.", nameof(codes));
            if (code != 0)
                packed[index >> 3] |= (byte)(1 << (7 - (index & 7)));
        }
    }

    private static void OptimiseIntervals(
        ReadOnlySpan<float> residuals,
        float normSquared,
        int points,
        ref float lower,
        ref float upper)
    {
        if (lower == upper)
            return;

        double initialLoss = Loss(residuals, lower, upper, points, normSquared);
        float scale = (1f - Lambda) / normSquared;
        if (!float.IsFinite(scale))
            return;

        for (int iteration = 0; iteration < OptimisationIterations; iteration++)
        {
            float stepInverse = (points - 1f) / (upper - lower);
            double daa = 0d;
            double dab = 0d;
            double dbb = 0d;
            double dax = 0d;
            double dbx = 0d;

            foreach (float residual in residuals)
            {
                float k = RoundLikeJava((Clamp(residual, lower, upper) - lower) * stepInverse);
                float s = k / (points - 1f);
                daa += (1d - s) * (1d - s);
                dab += (1d - s) * s;
                dbb += s * s;
                dax += residual * (1d - s);
                dbx += residual * s;
            }

            double m0 = scale * dax * dax + Lambda * daa;
            double m1 = scale * dax * dbx + Lambda * dab;
            double m2 = scale * dbx * dbx + Lambda * dbb;
            double determinant = m0 * m2 - m1 * m1;
            if (determinant == 0d || !double.IsFinite(determinant))
                return;

            float optimisedLower = (float)((m2 * dax - m1 * dbx) / determinant);
            float optimisedUpper = (float)((m0 * dbx - m1 * dax) / determinant);
            if (!float.IsFinite(optimisedLower) || !float.IsFinite(optimisedUpper) || optimisedLower > optimisedUpper)
                return;
            if (Math.Abs(lower - optimisedLower) < 1e-8d && Math.Abs(upper - optimisedUpper) < 1e-8d)
                return;

            double newLoss = Loss(residuals, optimisedLower, optimisedUpper, points, normSquared);
            if (!double.IsFinite(newLoss) || newLoss > initialLoss)
                return;

            lower = optimisedLower;
            upper = optimisedUpper;
            initialLoss = newLoss;
            if (lower == upper)
                return;
        }
    }

    private static double Loss(ReadOnlySpan<float> residuals, float lower, float upper, int points, float normSquared)
    {
        double step = ((double)upper - lower) / (points - 1f);
        if (step == 0d)
            return 0d;

        double stepInverse = 1d / step;
        double xe = 0d;
        double error = 0d;
        foreach (float value in residuals)
        {
            double reconstructed = lower + step * RoundLikeJava((float)((Clamp(value, lower, upper) - lower) * stepInverse));
            double difference = value - reconstructed;
            xe += value * difference;
            error += difference * difference;
        }
        return (1d - Lambda) * xe * xe / normSquared + Lambda * error;
    }

    private static float Clamp(float value, float minimum, float maximum)
        => value < minimum ? minimum : value > maximum ? maximum : value;
}
