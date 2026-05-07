using Rowles.LeanLucene.Codecs.Postings;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for Packed Int Codec.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class PackedIntCodecTests
{
    /// <summary>
    /// For each bit width 1-32, creates 128 values where the maximum fits in exactly
    /// that width, packs, unpacks, and verifies the round-trip produces identical values.
    /// </summary>
    [Fact(DisplayName = "Pack: Unpack All Bit Widths")]
    public void Pack_Unpack_AllBitWidths()
    {
        for (int bits = 1; bits <= 32; bits++)
        {
            var values = new int[PackedIntCodec.BlockSize];

            // Maximum unsigned value that fits in exactly `bits` bits.
            uint maxForWidth = bits == 32 ? uint.MaxValue : (1U << bits) - 1;
            values[0] = unchecked((int)maxForWidth);

            // Fill remaining slots with a smaller value to exercise mixed magnitudes.
            int fill = unchecked((int)(maxForWidth >> 1));
            for (int i = 1; i < values.Length; i++)
                values[i] = fill;

            var output = new byte[1 + bits * 16];
            int bytesWritten = PackedIntCodec.Pack(values, output);

            int numBits = output[0];
            Assert.Equal(bits, numBits);

            var unpacked = new int[PackedIntCodec.BlockSize];
            PackedIntCodec.Unpack(output.AsSpan(1, bytesWritten - 1), numBits, unpacked);

            Assert.Equal(values, unpacked);
        }
    }

    /// <summary>
    /// 128 zeros should produce numBits = 0 and a single-byte output.
    /// Round-trip must restore all zeros.
    /// </summary>
    [Fact(DisplayName = "Pack: Unpack All Zeros")]
    public void Pack_Unpack_AllZeros()
    {
        var values = new int[PackedIntCodec.BlockSize];

        var output = new byte[1 + 32 * 16]; // worst-case buffer
        int bytesWritten = PackedIntCodec.Pack(values, output);

        Assert.Equal(1, bytesWritten);
        Assert.Equal(0, output[0]);

        var unpacked = new int[PackedIntCodec.BlockSize];
        PackedIntCodec.Unpack(ReadOnlySpan<byte>.Empty, numBits: 0, unpacked);

        Assert.Equal(values, unpacked);
    }

    /// <summary>
    /// 128 copies of 42 must round-trip correctly.
    /// </summary>
    [Fact(DisplayName = "Pack: Unpack All Same Value")]
    public void Pack_Unpack_AllSameValue()
    {
        var values = new int[PackedIntCodec.BlockSize];
        Array.Fill(values, 42);

        var output = new byte[1 + 32 * 16];
        int bytesWritten = PackedIntCodec.Pack(values, output);
        int numBits = output[0];

        // 42 = 0b101010, so 6 bits are required.
        Assert.Equal(6, numBits);

        var unpacked = new int[PackedIntCodec.BlockSize];
        PackedIntCodec.Unpack(output.AsSpan(1, bytesWritten - 1), numBits, unpacked);

        Assert.Equal(values, unpacked);
    }

    /// <summary>
    /// 128 copies of <see cref="int.MaxValue"/> (0x7FFFFFFF) require 31 bits.
    /// Round-trip must restore every value exactly.
    /// </summary>
    [Fact(DisplayName = "Pack: Unpack Max Int")]
    public void Pack_Unpack_MaxInt()
    {
        var values = new int[PackedIntCodec.BlockSize];
        Array.Fill(values, int.MaxValue);

        var output = new byte[1 + 32 * 16];
        int bytesWritten = PackedIntCodec.Pack(values, output);
        int numBits = output[0];

        // int.MaxValue = 0x7FFFFFFF, so 31 bits are required.
        Assert.Equal(31, numBits);

        var unpacked = new int[PackedIntCodec.BlockSize];
        PackedIntCodec.Unpack(output.AsSpan(1, bytesWritten - 1), numBits, unpacked);

        Assert.Equal(values, unpacked);
    }

    /// <summary>
    /// Sorted ascending array [100, 105, 110, ...] with offset = 0.
    /// Delta-pack then delta-unpack must recover the original values.
    /// </summary>
    [Fact(DisplayName = "Pack Delta: Unpack Delta Sorted Input")]
    public void PackDelta_UnpackDelta_SortedInput()
    {
        var values = new int[PackedIntCodec.BlockSize];
        for (int i = 0; i < values.Length; i++)
            values[i] = 100 + i * 5;

        var output = new byte[1 + 32 * 16];
        var (numBits, bytesWritten) = PackedIntCodec.PackDelta(values, offset: 0, output);

        var unpacked = new int[PackedIntCodec.BlockSize];
        PackedIntCodec.UnpackDelta(output.AsSpan(1, bytesWritten - 1), numBits, offset: 0, unpacked);

        Assert.Equal(values, unpacked);
    }

    /// <summary>
    /// Sorted array starting at 1000 with offset = 900.
    /// First delta = 100, subsequent deltas = 3.
    /// </summary>
    [Fact(DisplayName = "Pack Delta: Unpack Delta With Offset")]
    public void PackDelta_UnpackDelta_WithOffset()
    {
        var values = new int[PackedIntCodec.BlockSize];
        for (int i = 0; i < values.Length; i++)
            values[i] = 1000 + i * 3;

        var output = new byte[1 + 32 * 16];
        var (numBits, bytesWritten) = PackedIntCodec.PackDelta(values, offset: 900, output);

        var unpacked = new int[PackedIntCodec.BlockSize];
        PackedIntCodec.UnpackDelta(output.AsSpan(1, bytesWritten - 1), numBits, offset: 900, unpacked);

        Assert.Equal(values, unpacked);
    }

    /// <summary>
    /// 128 sequential values [1, 2, 3, ..., 128] with offset = 0.
    /// All deltas equal 1, so numBits should be 1.
    /// </summary>
    [Fact(DisplayName = "Pack Delta: Single Element Block")]
    public void PackDelta_SingleElement_Block()
    {
        var values = new int[PackedIntCodec.BlockSize];
        for (int i = 0; i < values.Length; i++)
            values[i] = i + 1;

        var output = new byte[1 + 32 * 16];
        var (numBits, bytesWritten) = PackedIntCodec.PackDelta(values, offset: 0, output);

        // Every delta is 1, so only 1 bit is required.
        Assert.Equal(1, numBits);

        var unpacked = new int[PackedIntCodec.BlockSize];
        PackedIntCodec.UnpackDelta(output.AsSpan(1, bytesWritten - 1), numBits, offset: 0, unpacked);

        Assert.Equal(values, unpacked);
    }

    /// <summary>
    /// Verifies <see cref="PackedIntCodec.BitsRequired"/> for known boundary values.
    /// </summary>
    [Fact(DisplayName = "Bits Required: Returns Correct Width")]
    public void BitsRequired_ReturnsCorrectWidth()
    {
        Assert.Equal(0, PackedIntCodec.BitsRequired([0]));
        Assert.Equal(1, PackedIntCodec.BitsRequired([1]));
        Assert.Equal(2, PackedIntCodec.BitsRequired([2]));
        Assert.Equal(2, PackedIntCodec.BitsRequired([3]));
        Assert.Equal(3, PackedIntCodec.BitsRequired([7]));
        Assert.Equal(4, PackedIntCodec.BitsRequired([8]));
        Assert.Equal(4, PackedIntCodec.BitsRequired([15]));
        Assert.Equal(5, PackedIntCodec.BitsRequired([16]));
        Assert.Equal(7, PackedIntCodec.BitsRequired([127]));
        Assert.Equal(8, PackedIntCodec.BitsRequired([128]));
        Assert.Equal(8, PackedIntCodec.BitsRequired([255]));
        Assert.Equal(9, PackedIntCodec.BitsRequired([256]));
        Assert.Equal(31, PackedIntCodec.BitsRequired([int.MaxValue]));
    }

    /// <summary>
    /// Verifies that the output byte count equals <c>1 + numBits * 16</c> for every bit width.
    /// </summary>
    [Fact(DisplayName = "Pack: Output Size Is Correct")]
    public void Pack_OutputSize_IsCorrect()
    {
        for (int bits = 0; bits <= 32; bits++)
        {
            var values = new int[PackedIntCodec.BlockSize];
            if (bits > 0)
            {
                uint maxForWidth = bits == 32 ? uint.MaxValue : (1U << bits) - 1;
                values[0] = unchecked((int)maxForWidth);
            }

            var output = new byte[1 + 32 * 16];
            int bytesWritten = PackedIntCodec.Pack(values, output);

            int expectedSize = bits == 0 ? 1 : 1 + bits * 16;
            Assert.Equal(expectedSize, bytesWritten);
        }
    }

    /// <summary>
    /// Verifies Pack rejects blocks smaller than the fixed packed-int block size.
    /// </summary>
    [Fact(DisplayName = "Pack: Short Input Throws")]
    public void Pack_ShortInput_Throws()
    {
        var values = new int[PackedIntCodec.BlockSize - 1];
        var output = new byte[1 + 32 * 16];

        Assert.Throws<ArgumentException>(() => PackedIntCodec.Pack(values, output));
    }

    /// <summary>
    /// Verifies Pack rejects an output buffer that cannot hold non-zero bit-packed data.
    /// </summary>
    [Fact(DisplayName = "Pack: Small Output Throws")]
    public void Pack_SmallOutput_Throws()
    {
        var values = new int[PackedIntCodec.BlockSize];
        Array.Fill(values, 1);
        var output = new byte[1];

        Assert.Throws<ArgumentException>(() => PackedIntCodec.Pack(values, output));
    }

    /// <summary>
    /// Verifies Unpack rejects an output buffer smaller than the fixed block size.
    /// </summary>
    [Fact(DisplayName = "Unpack: Small Output Throws")]
    public void Unpack_SmallOutput_Throws()
    {
        var output = new int[PackedIntCodec.BlockSize - 1];

        Assert.Throws<ArgumentException>(() => PackedIntCodec.Unpack([], 0, output));
    }

    /// <summary>
    /// Verifies Unpack rejects invalid bit widths when the output buffer is valid.
    /// </summary>
    [Theory(DisplayName = "Unpack: Invalid Bit Width Throws")]
    [InlineData(-1)]
    [InlineData(33)]
    public void Unpack_InvalidBitWidth_Throws(int numBits)
    {
        var output = new int[PackedIntCodec.BlockSize];

        Assert.Throws<ArgumentOutOfRangeException>(() => PackedIntCodec.Unpack([], numBits, output));
    }

    /// <summary>
    /// Verifies PackDelta rejects blocks smaller than the fixed packed-int block size.
    /// </summary>
    [Fact(DisplayName = "Pack Delta: Short Input Throws")]
    public void PackDelta_ShortInput_Throws()
    {
        var values = new int[PackedIntCodec.BlockSize - 1];
        var output = new byte[1 + 32 * 16];

        Assert.Throws<ArgumentException>(() => PackedIntCodec.PackDelta(values, 0, output));
    }

    /// <summary>
    /// Verifies corrupt delta data that overflows during prefix-sum integration is rejected.
    /// </summary>
    [Fact(DisplayName = "Unpack Delta: Overflow Throws InvalidDataException")]
    public void UnpackDelta_Overflow_ThrowsInvalidDataException()
    {
        var deltas = new int[PackedIntCodec.BlockSize];
        deltas[0] = int.MaxValue;
        deltas[1] = 1;
        var packed = new byte[1 + 32 * 16];
        int bytesWritten = PackedIntCodec.Pack(deltas, packed);
        int numBits = packed[0];
        var output = new int[PackedIntCodec.BlockSize];

        Assert.Throws<InvalidDataException>(() =>
            PackedIntCodec.UnpackDelta(packed.AsSpan(1, bytesWritten - 1), numBits, offset: 1, output));
    }
}
