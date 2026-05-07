using Rowles.LeanLucene.Codecs.StoredFields;
using Rowles.LeanLucene.Compression.LZ4;
using Rowles.LeanLucene.Compression.Snappy;
using Rowles.LeanLucene.Compression.Zstandard;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Unit tests for the internal <see cref="StoredFieldCompression"/> helper covering
/// all <see cref="FieldCompressionPolicy"/> branches in both Compress and
/// Decompress, including round-trip correctness and empty-input handling.
/// </summary>
[Trait("Category", "Codecs")]
[Trait("Category", "UnitTest")]
public sealed class StoredFieldCompressionTests
{
    static StoredFieldCompressionTests()
    {
        Lz4Compression.Register();
        SnappyCompression.Register();
        ZstandardCompression.Register();
    }
    private static readonly byte[] SampleData =
        System.Text.Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog.");

    // ── None ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "StoredFieldCompression: None Policy Round-Trips")]
    public void None_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress(SampleData, FieldCompressionPolicy.None);
        var restored = StoredFieldCompression.Decompress(compressed, length, FieldCompressionPolicy.None);
        Assert.Equal(SampleData, restored);
    }

    [Fact(DisplayName = "StoredFieldCompression: None Policy Is A Copy")]
    public void None_IsCopy_NotSameInstance()
    {
        var (compressed, _) = StoredFieldCompression.Compress(SampleData, FieldCompressionPolicy.None);
        Assert.NotSame(SampleData, compressed);
        Assert.Equal(SampleData, compressed);
    }

    [Fact(DisplayName = "StoredFieldCompression: None Policy With Empty Input")]
    public void None_EmptyInput_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress([], FieldCompressionPolicy.None);
        Assert.Equal(0, length);
        var restored = StoredFieldCompression.Decompress(compressed, 0, FieldCompressionPolicy.None);
        Assert.Empty(restored);
    }

    // ── Deflate ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "StoredFieldCompression: Deflate Policy Round-Trips")]
    public void Deflate_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress(SampleData, FieldCompressionPolicy.Deflate);
        var restored = StoredFieldCompression.Decompress(compressed.AsSpan()[..length], SampleData.Length, FieldCompressionPolicy.Deflate);
        Assert.Equal(SampleData, restored);
    }

    // ── Lz4 ───────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "StoredFieldCompression: Lz4 Policy Round-Trips")]
    public void Lz4_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress(SampleData, FieldCompressionPolicy.Lz4);
        var restored = StoredFieldCompression.Decompress(compressed.AsSpan()[..length], SampleData.Length, FieldCompressionPolicy.Lz4);
        Assert.Equal(SampleData, restored);
    }

    // ── Zstandard ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "StoredFieldCompression: Zstandard Policy Round-Trips")]
    public void Zstandard_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress(SampleData, FieldCompressionPolicy.Zstandard);
        var restored = StoredFieldCompression.Decompress(compressed.AsSpan()[..length], SampleData.Length, FieldCompressionPolicy.Zstandard);
        Assert.Equal(SampleData, restored);
    }

    // ── Snappy ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "StoredFieldCompression: Snappy Policy Round-Trips")]
    public void Snappy_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress(SampleData, FieldCompressionPolicy.Snappy);
        var restored = StoredFieldCompression.Decompress(compressed.AsSpan()[..length], SampleData.Length, FieldCompressionPolicy.Snappy);
        Assert.Equal(SampleData, restored);
    }

    // ── Brotli (legacy) ───────────────────────────────────────────────────────

    [Fact(DisplayName = "StoredFieldCompression: Brotli Policy Round-Trips")]
    public void Brotli_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress(SampleData, FieldCompressionPolicy.Brotli);
        var restored = StoredFieldCompression.Decompress(compressed.AsSpan()[..length], SampleData.Length, FieldCompressionPolicy.Brotli);
        Assert.Equal(SampleData, restored);
    }

    // ── Decompress: None shortcut for None policy only ────────────────────────

    [Fact(DisplayName = "StoredFieldCompression: None Policy Shortcut On Empty Block")]
    public void Decompress_EmptyBlock_RoundTrips()
    {
        var (compressed, length) = StoredFieldCompression.Compress([], FieldCompressionPolicy.None);
        Assert.Equal(0, length);
        var restored = StoredFieldCompression.Decompress(compressed, 0, FieldCompressionPolicy.None);
        Assert.Empty(restored);
    }
}
