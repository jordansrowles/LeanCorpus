using System.Text;
using Rowles.LeanLucene.Codecs.Fst;

namespace Rowles.LeanLucene.Tests.Codecs.Fst;

/// <summary>
/// Tests for <see cref="FSTBuilder"/> — the minimal acyclic FST builder.
/// Since <c>FSTReader</c> does not yet exist, tests focus on:
/// correct <see cref="FSTBuilder.Count"/>, exception behaviour (sorted-order enforcement),
/// output byte array validity, and size sanity (suffix sharing compresses output).
/// </summary>
[Trait("Category", "Codecs")]
public sealed class FSTBuilderTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>
    /// Builds an FST from the given (string key, long output) pairs.
    /// Keys are NOT sorted here — caller must supply them in order.
    /// </summary>
    private static (byte[] Data, int Count) BuildFst(params (string Key, long Output)[] entries)
    {
        var builder = new FSTBuilder();
        foreach (var (key, output) in entries)
            builder.Add(Utf8(key), output);
        var data = builder.Finish();
        return (data, builder.Count);
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Build: Empty FST Produces Valid Output scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Empty FST Produces Valid Output")]
    public void Build_EmptyFST_ProducesValidOutput()
    {
        var builder = new FSTBuilder();
        var data = builder.Finish();

        Assert.NotNull(data);
        Assert.True(data.Length > 0, "Empty FST should still contain a header.");
        Assert.Equal(0, builder.Count);

        // Verify magic header bytes "FST1"
        Assert.Equal((byte)'F', data[0]);
        Assert.Equal((byte)'S', data[1]);
        Assert.Equal((byte)'T', data[2]);
        Assert.Equal((byte)'1', data[3]);
    }

    /// <summary>
    /// Verifies the Build: Single Key Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Single Key Round Trips")]
    public void Build_SingleKey_RoundTrips()
    {
        var builder = new FSTBuilder();
        builder.Add(Utf8("hello"), 42L);
        var data = builder.Finish();

        Assert.NotNull(data);
        Assert.True(data.Length > 4, "FST with one key should be larger than just the header magic.");
        Assert.Equal(1, builder.Count);
    }

    /// <summary>
    /// Verifies the Build: Multiple Keys Count Is Correct scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Multiple Keys Count Is Correct")]
    public void Build_MultipleKeys_CountIsCorrect()
    {
        var builder = new FSTBuilder();
        var keys = new[] { "alpha", "beta", "charlie", "delta", "echo",
                           "foxtrot", "golf", "hotel", "india", "juliet" };

        for (int i = 0; i < keys.Length; i++)
            builder.Add(Utf8(keys[i]), i * 100L);

        var data = builder.Finish();

        Assert.Equal(10, builder.Count);
        Assert.NotNull(data);
    }

    /// <summary>
    /// Verifies the Build: Unsorted Keys Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Unsorted Keys Throws")]
    public void Build_UnsortedKeys_Throws()
    {
        var builder = new FSTBuilder();
        builder.Add(Utf8("b"), 1L);

        var ex = Assert.Throws<ArgumentException>(() => builder.Add(Utf8("a"), 2L));
        Assert.Contains("sorted order", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the Build: Duplicate Keys Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Duplicate Keys Throws")]
    public void Build_DuplicateKeys_Throws()
    {
        var builder = new FSTBuilder();
        builder.Add(Utf8("same"), 1L);

        var ex = Assert.Throws<ArgumentException>(() => builder.Add(Utf8("same"), 2L));
        Assert.Contains("Duplicate", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies the Build: Output Distribution No Data Loss scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Output Distribution No Data Loss")]
    public void Build_OutputDistribution_NoDataLoss()
    {
        // Two keys sharing prefix "ab" with different outputs.
        // The output distribution algorithm should split correctly
        // without throwing or losing data.
        var (data, count) = BuildFst(
            ("abc", 100L),
            ("abd", 200L)
        );

        Assert.Equal(2, count);
        Assert.NotNull(data);
        Assert.True(data.Length > 4);
    }

    /// <summary>
    /// Verifies the Build: Suffix Sharing Compresses Output scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Suffix Sharing Compresses Output")]
    public void Build_SuffixSharing_CompressesOutput()
    {
        // Keys with shared suffixes should produce a smaller FST than
        // if each key were stored independently.
        var keysWithSharedSuffixes = new[]
        {
            "testing", "toasting",   // shared suffix "sting"/"asting"
            "raining", "running"     // shared suffix "ning"/"nning"
        };
        Array.Sort(keysWithSharedSuffixes, StringComparer.Ordinal);

        var builder = new FSTBuilder();
        for (int i = 0; i < keysWithSharedSuffixes.Length; i++)
            builder.Add(Utf8(keysWithSharedSuffixes[i]), (long)(i * 10));
        var fstData = builder.Finish();

        // Naive size: sum of all key bytes + overhead per key
        int naiveSize = 0;
        foreach (var k in keysWithSharedSuffixes)
            naiveSize += Encoding.UTF8.GetByteCount(k) + 16; // key bytes + generous overhead

        Assert.True(fstData.Length < naiveSize,
            $"FST size ({fstData.Length}B) should be smaller than naive ({naiveSize}B) due to suffix sharing.");
        Assert.Equal(4, builder.Count);
    }

    /// <summary>
    /// Verifies the Build: Large Scale 10 K Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Large Scale 10 K Terms")]
    public void Build_LargeScale_10KTerms()
    {
        // Generate 10K sorted random-ish keys
        var keys = new List<string>(10_000);
        for (int i = 0; i < 10_000; i++)
            keys.Add($"term_{i:D6}");

        keys.Sort(StringComparer.Ordinal);

        var builder = new FSTBuilder();
        for (int i = 0; i < keys.Count; i++)
            builder.Add(Utf8(keys[i]), (long)i * 8);

        var data = builder.Finish();

        Assert.Equal(10_000, builder.Count);
        Assert.NotNull(data);
        Assert.True(data.Length > 0);

        // The FST should be significantly smaller than the raw key data
        int rawKeyBytes = 0;
        foreach (var k in keys)
            rawKeyBytes += Encoding.UTF8.GetByteCount(k);

        Assert.True(data.Length < rawKeyBytes,
            $"FST ({data.Length}B) should compress better than raw keys ({rawKeyBytes}B) due to prefix/suffix sharing.");
    }

    /// <summary>
    /// Verifies the Build: Single Byte Keys scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Single Byte Keys")]
    public void Build_SingleByteKeys()
    {
        var builder = new FSTBuilder();
        builder.Add([(byte)'a'], 10L);
        builder.Add([(byte)'b'], 20L);
        builder.Add([(byte)'c'], 30L);
        var data = builder.Finish();

        Assert.Equal(3, builder.Count);
        Assert.NotNull(data);
        Assert.True(data.Length > 4);
    }

    /// <summary>
    /// Verifies the Build: Empty Key scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Empty Key")]
    public void Build_EmptyKey()
    {
        // Empty byte sequence is a valid key (represents the empty string term)
        var builder = new FSTBuilder();
        builder.Add(ReadOnlySpan<byte>.Empty, 99L);
        var data = builder.Finish();

        Assert.Equal(1, builder.Count);
        Assert.NotNull(data);
    }

    /// <summary>
    /// Verifies the Build: Long Keys scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Long Keys")]
    public void Build_LongKeys()
    {
        // Keys longer than 256 bytes
        var longKey1 = new string('a', 300);
        var longKey2 = new string('b', 300);

        var builder = new FSTBuilder();
        builder.Add(Utf8(longKey1), 1L);
        builder.Add(Utf8(longKey2), 2L);
        var data = builder.Finish();

        Assert.Equal(2, builder.Count);
        Assert.NotNull(data);
        Assert.True(data.Length > 4);
    }

    /// <summary>
    /// Verifies the Build: Binary Keys scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Binary Keys")]
    public void Build_BinaryKeys()
    {
        // Keys containing 0x00 and 0xFF bytes
        var builder = new FSTBuilder();
        builder.Add([0x00], 1L);
        builder.Add([0x00, 0xFF], 2L);
        builder.Add([0x01], 3L);
        builder.Add([0xFF], 4L);
        builder.Add([0xFF, 0x00], 5L);
        builder.Add([0xFF, 0xFF], 6L);
        var data = builder.Finish();

        Assert.Equal(6, builder.Count);
        Assert.NotNull(data);
    }

    // ── Additional edge-case tests ──────────────────────────────────────────

    /// <summary>
    /// Verifies the Build: Cannot Add After Finish scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Cannot Add After Finish")]
    public void Build_CannotAddAfterFinish()
    {
        var builder = new FSTBuilder();
        builder.Add(Utf8("a"), 1L);
        _ = builder.Finish();

        Assert.Throws<InvalidOperationException>(() =>
            builder.Add(Utf8("b"), 2L));
    }

    /// <summary>
    /// Verifies the Build: Cannot Finish Twice scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Cannot Finish Twice")]
    public void Build_CannotFinishTwice()
    {
        var builder = new FSTBuilder();
        _ = builder.Finish();

        Assert.Throws<InvalidOperationException>(() => builder.Finish());
    }

    /// <summary>
    /// Verifies the Build: Shared Prefixes Compresses Output scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Shared Prefixes Compresses Output")]
    public void Build_SharedPrefixes_CompressesOutput()
    {
        // Many keys sharing the prefix "prefix_" should benefit from prefix sharing
        var builder = new FSTBuilder();
        for (int i = 0; i < 100; i++)
            builder.Add(Utf8($"prefix_{i:D4}"), (long)i);

        var data = builder.Finish();
        Assert.Equal(100, builder.Count);

        // Each key is ~11 bytes ("prefix_0000"). Without sharing, that's 1100+ bytes.
        // With prefix sharing the FST should be smaller.
        int rawSize = 100 * 11;
        Assert.True(data.Length < rawSize,
            $"FST ({data.Length}B) should be smaller than raw keys ({rawSize}B) with prefix sharing.");
    }

    /// <summary>
    /// Verifies the Build: Zero Outputs Succeeds scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Zero Outputs Succeeds")]
    public void Build_ZeroOutputs_Succeeds()
    {
        // All outputs are zero — should still build correctly
        var builder = new FSTBuilder();
        builder.Add(Utf8("alpha"), 0L);
        builder.Add(Utf8("beta"), 0L);
        builder.Add(Utf8("gamma"), 0L);
        var data = builder.Finish();

        Assert.Equal(3, builder.Count);
        Assert.NotNull(data);
    }

    /// <summary>
    /// Verifies the Build: Large Output Values Succeeds scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Large Output Values Succeeds")]
    public void Build_LargeOutputValues_Succeeds()
    {
        // Outputs that require full 10-byte VarInt encoding
        var builder = new FSTBuilder();
        builder.Add(Utf8("key1"), long.MaxValue / 2);
        builder.Add(Utf8("key2"), long.MaxValue);
        var data = builder.Finish();

        Assert.Equal(2, builder.Count);
        Assert.NotNull(data);
    }

    /// <summary>
    /// Verifies the Build: Header Contains Magic scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Header Contains Magic")]
    public void Build_HeaderContainsMagic()
    {
        var (data, _) = BuildFst(("hello", 1L));

        // First 4 bytes must be "FST1"
        Assert.Equal("FST1", Encoding.ASCII.GetString(data, 0, 4));
    }

    /// <summary>
    /// Verifies the Build: Monotonically Increasing Outputs Succeeds scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Monotonically Increasing Outputs Succeeds")]
    public void Build_MonotonicallyIncreasingOutputs_Succeeds()
    {
        // Simulates real postings offsets — strictly increasing
        var builder = new FSTBuilder();
        long offset = 0;
        var terms = new[] { "apple", "banana", "cherry", "date", "elderberry",
                            "fig", "grape", "honeydew", "jackfruit", "kiwi" };

        foreach (var term in terms)
        {
            builder.Add(Utf8(term), offset);
            offset += 128; // typical postings block size
        }

        var data = builder.Finish();
        Assert.Equal(terms.Length, builder.Count);
        Assert.NotNull(data);
    }

    /// <summary>
    /// Verifies the Build: VarInt Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Build: VarInt Round Trips")]
    public void Build_VarInt_RoundTrips()
    {
        // Directly test VarInt encoding/decoding for critical values
        var values = new long[]
        {
            0, 1, 127, 128, 255, 256, 16383, 16384,
            int.MaxValue, long.MaxValue / 2, long.MaxValue,
            -1 // encoded as unsigned, should round-trip
        };

        var buffer = new byte[20];
        foreach (long value in values)
        {
            int written = FSTBuilder.WriteVarInt(buffer, 0, value);
            int offset = 0;
            long decoded = FSTBuilder.ReadVarInt(buffer.AsSpan(), ref offset);

            Assert.Equal(value, decoded);
            Assert.Equal(written, offset);
        }
    }

    /// <summary>
    /// Verifies the Build: Small Initial Capacity Grows Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Build: Small Initial Capacity Grows Correctly")]
    public void Build_SmallInitialCapacity_GrowsCorrectly()
    {
        // Use a tiny initial capacity to exercise buffer growth
        var builder = new FSTBuilder(initialCapacity: 16);
        for (int i = 0; i < 100; i++)
            builder.Add(Utf8($"key_{i:D4}"), (long)i);

        var data = builder.Finish();
        Assert.Equal(100, builder.Count);
        Assert.NotNull(data);
    }
}
