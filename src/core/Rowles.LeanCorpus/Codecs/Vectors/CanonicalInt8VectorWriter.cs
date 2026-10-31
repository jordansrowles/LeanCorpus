using System.Buffers;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>Encodes stable per-vector unsigned Int8 canonical records.</summary>
internal static class CanonicalInt8VectorWriter
{
    /// <summary>
    /// Quantises one vector with per-vector affine parameters. Rounding is explicit
    /// nearest with ties upward, and the caller retains ownership of both spans.
    /// </summary>
    internal static CanonicalInt8VectorMetadata Encode(
        ReadOnlySpan<float> vector,
        Span<byte> codes)
    {
        ArgumentOutOfRangeException.ThrowIfZero(vector.Length);
        if (codes.Length != vector.Length)
            throw new ArgumentException($"Code destination length {codes.Length} != vector dimension {vector.Length}.", nameof(codes));

        float min = float.MaxValue;
        float max = float.MinValue;
        for (int index = 0; index < vector.Length; index++)
        {
            float value = vector[index];
            if (!float.IsFinite(value))
                throw new ArgumentException("Canonical Int8 vectors must contain only finite values.", nameof(vector));
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }

        double range = (double)max - min;
        float scale = range == 0d ? 0f : (float)(range / byte.MaxValue);
        if (range > 0d && scale == 0f)
            scale = float.Epsilon;
        if (!float.IsFinite(scale))
            throw new ArgumentException("Canonical Int8 scale is outside the supported finite range.", nameof(vector));

        // Keep the decoded endpoint finite when float rounding nudges scale upward.
        while (scale > 0f && (double)min + (double)scale * byte.MaxValue > float.MaxValue)
            scale = MathF.BitDecrement(scale);

        int sumOfCodes = 0;
        double decodedSquaredNorm = 0d;
        for (int index = 0; index < vector.Length; index++)
        {
            byte code;
            if (range == 0d)
            {
                code = 0;
            }
            else
            {
                double position = (((double)vector[index] - min) / range) * byte.MaxValue;
                code = (byte)Math.Clamp(Math.Floor(position + 0.5d), 0d, byte.MaxValue);
            }

            codes[index] = code;
            sumOfCodes = checked(sumOfCodes + code);
            float decoded = Decode(min, scale, code);
            decodedSquaredNorm += (double)decoded * decoded;
        }

        if (!double.IsFinite(decodedSquaredNorm) || decodedSquaredNorm > float.MaxValue)
            throw new ArgumentException("Canonical decoded squared norm exceeds the supported metadata range.", nameof(vector));

        return new CanonicalInt8VectorMetadata(min, scale, sumOfCodes, (float)decodedSquaredNorm);
    }

    /// <summary>
    /// Writes one metadata-plus-code record. This interleaved form is a bounded
    /// temporary spool; the final .vq body writes the same rows as separate arrays.
    /// </summary>
    internal static void WriteRecord(
        ISequentialIndexOutput output,
        ReadOnlySpan<float> vector,
        Span<byte> codeScratch)
    {
        ArgumentNullException.ThrowIfNull(output);
        CanonicalInt8VectorMetadata metadata = Encode(vector, codeScratch);
        WriteMetadata(output, metadata);
        output.WriteBytes(codeScratch);
    }

    internal static void WriteMetadata(ISequentialIndexOutput output, CanonicalInt8VectorMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.WriteInt32(BitConverter.SingleToInt32Bits(metadata.Offset));
        output.WriteInt32(BitConverter.SingleToInt32Bits(metadata.Scale));
        output.WriteInt32(metadata.SumOfCodes);
        output.WriteInt32(BitConverter.SingleToInt32Bits(metadata.DecodedSquaredNorm));
    }

    /// <summary>
    /// Quantises each present source row once into a bounded interleaved scratch file.
    /// Missing rows receive deterministic zero records. The caller can map this spool
    /// for centroid/OSQ passes and later stream metadata and codes into their v3 arrays.
    /// </summary>
    internal static void WriteRecords(
        string filePath,
        int docCount,
        int dimension,
        IVectorSource vectorsByDoc,
        IReadOnlyList<int> vectorDocIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(docCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dimension);
        ArgumentNullException.ThrowIfNull(vectorsByDoc);
        ArgumentNullException.ThrowIfNull(vectorDocIds);
        if (vectorsByDoc.Dimension != dimension || vectorsByDoc.Count < docCount)
            throw new ArgumentException("Vector source dimensions do not match the destination field.", nameof(vectorsByDoc));

        byte[] presence = VectorPresence.Create(docCount, vectorDocIds);
        long recordSize = checked((long)CanonicalInt8VectorMetadata.Size + dimension);
        long expectedLength = checked(docCount * recordSize);
        float[] vectorBuffer = ArrayPool<float>.Shared.Rent(dimension);
        byte[] codeBuffer = ArrayPool<byte>.Shared.Rent(dimension);
        string temporaryPath = string.Concat(filePath, ".", Guid.NewGuid().ToString("N"), ".canonical.tmp");
        try
        {
            using (var output = new IndexOutput(temporaryPath, preallocationSize: expectedLength))
            {
                Span<float> vector = vectorBuffer.AsSpan(0, dimension);
                Span<byte> codes = codeBuffer.AsSpan(0, dimension);
                Span<byte> zeroMetadata = stackalloc byte[CanonicalInt8VectorMetadata.Size];
                zeroMetadata.Clear();
                for (int docId = 0; docId < docCount; docId++)
                {
                    if ((presence[docId >> 3] & (1 << (docId & 7))) == 0)
                    {
                        output.WriteBytes(zeroMetadata);
                        codes.Clear();
                        output.WriteBytes(codes);
                        continue;
                    }

                    vectorsByDoc.CopyVectorTo(docId, vector);
                    WriteRecord(output, vector, codes);
                }
            }

            File.Move(temporaryPath, filePath, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Preserve the original write failure; the uniquely named spool is not published.
            }
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(codeBuffer, clearArray: true);
            ArrayPool<float>.Shared.Return(vectorBuffer, clearArray: true);
        }
    }

    private static float Decode(float offset, float scale, byte code)
        => (float)((double)offset + (double)scale * code);
}
