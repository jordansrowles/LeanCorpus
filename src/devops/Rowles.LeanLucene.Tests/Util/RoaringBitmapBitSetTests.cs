using Rowles.LeanLucene.Util;

namespace Rowles.LeanLucene.Tests.Util;

/// <summary>
/// Unit tests for <see cref="RoaringBitmapBitSet"/>.
/// </summary>
[Trait("Category", "Util")]
[Trait("Category", "UnitTest")]
public sealed class RoaringBitmapBitSetTests
{
    /// <summary>
    /// Verifies the Contains: Returns True For Added Doc Id scenario.
    /// </summary>
    [Fact(DisplayName = "Contains: Returns True For Added Doc Id")]
    public void Contains_ReturnsTrueForAddedDocId()
    {
        var bitmap = new RoaringBitmap();
        bitmap.Add(42);
        var bitSet = new RoaringBitmapBitSet(bitmap);

        Assert.True(bitSet.Contains(42));
    }

    /// <summary>
    /// Verifies the Contains: Returns False For Missing Doc Id scenario.
    /// </summary>
    [Fact(DisplayName = "Contains: Returns False For Missing Doc Id")]
    public void Contains_ReturnsFalseForMissingDocId()
    {
        var bitmap = new RoaringBitmap();
        bitmap.Add(42);
        var bitSet = new RoaringBitmapBitSet(bitmap);

        Assert.False(bitSet.Contains(43));
    }

    /// <summary>
    /// Verifies the Cardinality: Returns Number Of Set Bits scenario.
    /// </summary>
    [Fact(DisplayName = "Cardinality: Returns Number Of Set Bits")]
    public void Cardinality_ReturnsNumberOfSetBits()
    {
        var bitmap = new RoaringBitmap();
        bitmap.Add(1);
        bitmap.Add(100);
        bitmap.Add(65536);
        var bitSet = new RoaringBitmapBitSet(bitmap);

        Assert.Equal(3, bitSet.Cardinality);
    }

    /// <summary>
    /// Verifies the Cardinality: Returns Zero For Empty Bitmap scenario.
    /// </summary>
    [Fact(DisplayName = "Cardinality: Returns Zero For Empty Bitmap")]
    public void Cardinality_ReturnsZero_ForEmptyBitmap()
    {
        var bitSet = new RoaringBitmapBitSet(new RoaringBitmap());
        Assert.Equal(0, bitSet.Cardinality);
    }

    /// <summary>
    /// Verifies the Constructor: Throws On Null Bitmap scenario.
    /// </summary>
    [Fact(DisplayName = "Constructor: Throws On Null Bitmap")]
    public void Constructor_ThrowsOnNullBitmap()
    {
        Assert.Throws<ArgumentNullException>(() => new RoaringBitmapBitSet(null!));
    }

    /// <summary>
    /// Verifies the Contains: Returns True For Large Doc Id scenario.
    /// </summary>
    [Fact(DisplayName = "Contains: Returns True For Large Doc Id")]
    public void Contains_ReturnsTrueForLargeDocId()
    {
        var bitmap = new RoaringBitmap();
        bitmap.Add(1_000_000);
        var bitSet = new RoaringBitmapBitSet(bitmap);

        Assert.True(bitSet.Contains(1_000_000));
        Assert.False(bitSet.Contains(1_000_001));
    }

    /// <summary>
    /// Verifies the Contains: Returns True For Zero Doc Id scenario.
    /// </summary>
    [Fact(DisplayName = "Contains: Returns True For Zero Doc Id")]
    public void Contains_ReturnsTrueForZeroDocId()
    {
        var bitmap = new RoaringBitmap();
        bitmap.Add(0);
        var bitSet = new RoaringBitmapBitSet(bitmap);

        Assert.True(bitSet.Contains(0));
        Assert.False(bitSet.Contains(1));
    }

    /// <summary>
    /// Verifies the Cardinality: Reflects Bitmap After Multiple Adds scenario.
    /// </summary>
    [Fact(DisplayName = "Cardinality: Reflects Bitmap After Multiple Adds")]
    public void Cardinality_ReflectsBitmapAfterMultipleAdds()
    {
        var bitmap = new RoaringBitmap();
        for (int i = 0; i < 100; i++)
            bitmap.Add(i * 2); // even numbers 0..198

        var bitSet = new RoaringBitmapBitSet(bitmap);
        Assert.Equal(100, bitSet.Cardinality);
    }
}
