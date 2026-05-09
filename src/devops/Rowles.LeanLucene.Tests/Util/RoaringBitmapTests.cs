namespace Rowles.LeanLucene.Tests.Util;

using Rowles.LeanLucene.Util;

/// <summary>
/// Contains unit tests for Roaring Bitmap.
/// </summary>
public sealed class RoaringBitmapTests
{
    /// <summary>
    /// Verifies the Add: Contains Single Value scenario.
    /// </summary>
    [Fact(DisplayName = "Add: Contains Single Value")]
    public void Add_Contains_SingleValue()
    {
        var rb = new RoaringBitmap();
        rb.Add(42);
        Assert.True(rb.Contains(42));
        Assert.False(rb.Contains(43));
    }

    /// <summary>
    /// Verifies the Add: Contains Multiple Values scenario.
    /// </summary>
    [Fact(DisplayName = "Add: Contains Multiple Values")]
    public void Add_Contains_MultipleValues()
    {
        var rb = new RoaringBitmap();
        var values = new[] { 5, 100, 999, 65536, 200_000, 1_000_000 };
        foreach (var v in values)
            rb.Add(v);

        foreach (var v in values)
            Assert.True(rb.Contains(v), $"Should contain {v}");

        Assert.False(rb.Contains(6));
        Assert.False(rb.Contains(101));
    }

    /// <summary>
    /// Verifies the Cardinality: Reflects Added Values scenario.
    /// </summary>
    [Fact(DisplayName = "Cardinality: Reflects Added Values")]
    public void Cardinality_ReflectsAddedValues()
    {
        var rb = new RoaringBitmap();
        for (int i = 0; i < 50; i++)
            rb.Add(i * 7);

        Assert.Equal(50, rb.Cardinality);
    }

    /// <summary>
    /// Verifies the Remove: Decreases Cardinality scenario.
    /// </summary>
    [Fact(DisplayName = "Remove: Decreases Cardinality")]
    public void Remove_DecreasesCardinality()
    {
        var rb = new RoaringBitmap();
        rb.Add(10);
        rb.Add(20);
        rb.Add(30);
        Assert.Equal(3, rb.Cardinality);

        Assert.True(rb.Remove(20));
        Assert.Equal(2, rb.Cardinality);
        Assert.False(rb.Contains(20));
    }

    /// <summary>
    /// Verifies the Remove: Non Existent Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Remove: Non Existent Returns False")]
    public void Remove_NonExistent_ReturnsFalse()
    {
        var rb = new RoaringBitmap();
        rb.Add(1);
        Assert.False(rb.Remove(999));
        Assert.Equal(1, rb.Cardinality);
    }

    /// <summary>
    /// Verifies the Array Container: Converts To Bitmap Above 4096 scenario.
    /// </summary>
    [Fact(DisplayName = "Array Container: Converts To Bitmap Above 4096")]
    public void ArrayContainer_ConvertsToBitmap_Above4096()
    {
        var rb = new RoaringBitmap();
        // Add 4097 values in the same 64K chunk (chunk 0)
        for (int i = 0; i < 4097; i++)
            rb.Add(i);

        Assert.Equal(4097, rb.Cardinality);
        // Verify all values present
        for (int i = 0; i < 4097; i++)
            Assert.True(rb.Contains(i), $"Should contain {i}");
    }

    /// <summary>
    /// Verifies the Bitmap Container: Converts To Array Below 4096 scenario.
    /// </summary>
    [Fact(DisplayName = "Bitmap Container: Converts To Array Below 4096")]
    public void BitmapContainer_ConvertsToArray_Below4096()
    {
        var rb = new RoaringBitmap();
        // Add 5000 values to force bitmap
        for (int i = 0; i < 5000; i++)
            rb.Add(i);

        // Remove down to 4000
        for (int i = 4999; i >= 4000; i--)
            rb.Remove(i);

        Assert.Equal(4000, rb.Cardinality);
        for (int i = 0; i < 4000; i++)
            Assert.True(rb.Contains(i));
    }

    /// <summary>
    /// Verifies the Add Range: Adds Consecutive Doc IDs scenario.
    /// </summary>
    [Fact(DisplayName = "Add Range: Adds Consecutive Doc IDs")]
    public void AddRange_AddsConsecutiveDocIds()
    {
        var rb = new RoaringBitmap();
        rb.AddRange(1000, 2000);

        Assert.Equal(1000, rb.Cardinality);
        Assert.False(rb.Contains(999));
        Assert.True(rb.Contains(1000));
        Assert.True(rb.Contains(1999));
        Assert.False(rb.Contains(2000));
    }

    /// <summary>
    /// Verifies the Cross Container: Values In Multiple Chunks scenario.
    /// </summary>
    [Fact(DisplayName = "Cross Container: Values In Multiple Chunks")]
    public void CrossContainer_ValuesInMultipleChunks()
    {
        var rb = new RoaringBitmap();
        // Values in chunks 0, 1, 5, and 100
        int[] vals = { 0, 65536, 5 * 65536, 100 * 65536 };
        foreach (var v in vals)
            rb.Add(v);

        Assert.Equal(4, rb.Cardinality);
        foreach (var v in vals)
            Assert.True(rb.Contains(v));
    }

    /// <summary>
    /// Verifies the Enumeration: Yields In Sorted Order scenario.
    /// </summary>
    [Fact(DisplayName = "Enumeration: Yields In Sorted Order")]
    public void Enumeration_YieldsInSortedOrder()
    {
        var rb = new RoaringBitmap();
        // Add out of order, across multiple chunks
        rb.Add(200_000);
        rb.Add(10);
        rb.Add(100_000);
        rb.Add(5);
        rb.Add(65536);

        var result = rb.ToList();
        Assert.Equal(5, result.Count);
        for (int i = 1; i < result.Count; i++)
            Assert.True(result[i] > result[i - 1], $"Not sorted: {result[i - 1]} >= {result[i]}");
    }

    /// <summary>
    /// Verifies the Large Scale: One Million Doc IDs scenario.
    /// </summary>
    [Fact(DisplayName = "Large Scale: One Million Doc IDs")]
    public void LargeScale_OneMillion_DocIds()
    {
        var rb = new RoaringBitmap();
        var rng = new Random(42);
        var added = new HashSet<int>();

        for (int i = 0; i < 1_000_000; i++)
        {
            int v = rng.Next(0, 10_000_000);
            if (added.Add(v))
                rb.Add(v);
        }

        Assert.Equal(added.Count, rb.Cardinality);

        // Spot-check 1000 random values
        var sample = added.Take(1000).ToList();
        foreach (var v in sample)
            Assert.True(rb.Contains(v));
    }

    /// <summary>
    /// Verifies the Optimise Runs: Converts Consecutive Ranges scenario.
    /// </summary>
    [Fact(DisplayName = "Optimise Runs: Converts Consecutive Ranges")]
    public void OptimiseRuns_ConvertsConsecutiveRanges()
    {
        var rb = new RoaringBitmap();
        rb.AddRange(100, 600);

        int cardBefore = rb.Cardinality;
        rb.OptimiseRuns();

        Assert.Equal(cardBefore, rb.Cardinality);
        Assert.True(rb.Contains(100));
        Assert.True(rb.Contains(599));
        Assert.False(rb.Contains(600));
    }

    /// <summary>
    /// Verifies the Is Empty: When New Is True scenario.
    /// </summary>
    [Fact(DisplayName = "Is Empty: When New Is True")]
    public void IsEmpty_WhenNew_IsTrue()
    {
        var rb = new RoaringBitmap();
        Assert.True(rb.IsEmpty);
    }

    /// <summary>
    /// Verifies the Is Empty: After Add And Remove All Is True scenario.
    /// </summary>
    [Fact(DisplayName = "Is Empty: After Add And Remove All Is True")]
    public void IsEmpty_AfterAddAndRemoveAll_IsTrue()
    {
        var rb = new RoaringBitmap();
        rb.Add(42);
        Assert.False(rb.IsEmpty);
        rb.Remove(42);
        Assert.True(rb.IsEmpty);
    }

    /// <summary>
    /// Verifies the Duplicate Add: Does Not Increase Cardinality scenario.
    /// </summary>
    [Fact(DisplayName = "Duplicate Add: Does Not Increase Cardinality")]
    public void DuplicateAdd_DoesNotIncreaseCardinality()
    {
        var rb = new RoaringBitmap();
        rb.Add(100);
        rb.Add(100);
        rb.Add(100);
        Assert.Equal(1, rb.Cardinality);
    }

    /// <summary>
    /// Verifies the And: Returns Intersection scenario.
    /// </summary>
    [Fact(DisplayName = "And: Returns Intersection")]
    public void And_ReturnsIntersection()
    {
        var a = new RoaringBitmap();
        var b = new RoaringBitmap();
        for (int i = 0; i < 100; i++) a.Add(i);
        for (int i = 50; i < 150; i++) b.Add(i);

        var result = RoaringBitmap.And(a, b);
        Assert.Equal(50, result.Cardinality);
        for (int i = 50; i < 100; i++)
            Assert.True(result.Contains(i));
        Assert.False(result.Contains(49));
        Assert.False(result.Contains(100));
    }

    /// <summary>
    /// Verifies the Or: Returns Union scenario.
    /// </summary>
    [Fact(DisplayName = "Or: Returns Union")]
    public void Or_ReturnsUnion()
    {
        var a = new RoaringBitmap();
        var b = new RoaringBitmap();
        for (int i = 0; i < 100; i++) a.Add(i);
        for (int i = 50; i < 150; i++) b.Add(i);

        var result = RoaringBitmap.Or(a, b);
        Assert.Equal(150, result.Cardinality);
        for (int i = 0; i < 150; i++)
            Assert.True(result.Contains(i));
    }

    /// <summary>
    /// Verifies the And Not: Returns Difference scenario.
    /// </summary>
    [Fact(DisplayName = "And Not: Returns Difference")]
    public void AndNot_ReturnsDifference()
    {
        var a = new RoaringBitmap();
        var b = new RoaringBitmap();
        for (int i = 0; i < 100; i++) a.Add(i);
        for (int i = 50; i < 150; i++) b.Add(i);

        var result = RoaringBitmap.AndNot(a, b);
        Assert.Equal(50, result.Cardinality);
        for (int i = 0; i < 50; i++)
            Assert.True(result.Contains(i));
        Assert.False(result.Contains(50));
    }

    /// <summary>
    /// Verifies the Xor: Returns Symmetric Difference scenario.
    /// </summary>
    [Fact(DisplayName = "Xor: Returns Symmetric Difference")]
    public void Xor_ReturnsSymmetricDifference()
    {
        var a = new RoaringBitmap();
        var b = new RoaringBitmap();
        for (int i = 0; i < 100; i++) a.Add(i);
        for (int i = 50; i < 150; i++) b.Add(i);

        var result = RoaringBitmap.Xor(a, b);
        Assert.Equal(100, result.Cardinality);
        for (int i = 0; i < 50; i++)
            Assert.True(result.Contains(i));
        for (int i = 100; i < 150; i++)
            Assert.True(result.Contains(i));
        for (int i = 50; i < 100; i++)
            Assert.False(result.Contains(i));
    }

    /// <summary>
    /// Verifies the And: Commutativity scenario.
    /// </summary>
    [Fact(DisplayName = "And: Commutativity")]
    public void And_Commutativity()
    {
        var a = new RoaringBitmap();
        var b = new RoaringBitmap();
        var rng = new Random(123);
        for (int i = 0; i < 500; i++) a.Add(rng.Next(0, 10000));
        for (int i = 0; i < 500; i++) b.Add(rng.Next(0, 10000));

        var ab = RoaringBitmap.And(a, b);
        var ba = RoaringBitmap.And(b, a);
        Assert.Equal(ab.Cardinality, ba.Cardinality);
    }

    /// <summary>
    /// Verifies the Or: With Empty Returns Original scenario.
    /// </summary>
    [Fact(DisplayName = "Or: With Empty Returns Original")]
    public void Or_WithEmpty_ReturnsOriginal()
    {
        var a = new RoaringBitmap();
        for (int i = 0; i < 100; i++) a.Add(i * 3);
        var empty = new RoaringBitmap();

        var result = RoaringBitmap.Or(a, empty);
        Assert.Equal(a.Cardinality, result.Cardinality);
    }

    /// <summary>
    /// Verifies the And: With Empty Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "And: With Empty Returns Empty")]
    public void And_WithEmpty_ReturnsEmpty()
    {
        var a = new RoaringBitmap();
        for (int i = 0; i < 100; i++) a.Add(i);
        var empty = new RoaringBitmap();

        var result = RoaringBitmap.And(a, empty);
        Assert.Equal(0, result.Cardinality);
    }

    /// <summary>
    /// Verifies the Set Ops: Cross Chunk scenario.
    /// </summary>
    [Fact(DisplayName = "Set Ops: Cross Chunk")]
    public void SetOps_CrossChunk()
    {
        var a = new RoaringBitmap();
        var b = new RoaringBitmap();
        // a: chunk 0 + chunk 1
        a.AddRange(0, 100);
        a.AddRange(65536, 65636);
        // b: chunk 0 + chunk 2
        b.AddRange(50, 150);
        b.AddRange(131072, 131172);

        var and = RoaringBitmap.And(a, b);
        Assert.Equal(50, and.Cardinality); // overlap in chunk 0 only

        var or = RoaringBitmap.Or(a, b);
        Assert.Equal(350, or.Cardinality); // 150 + 100 + 100
    }

    /// <summary>
    /// Verifies the Serialise: Round-trip Array Container scenario.
    /// </summary>
    [Fact(DisplayName = "Serialise: Round-trip Array Container")]
    public void Serialise_RoundTrip_ArrayContainer()
    {
        var rb = new RoaringBitmap();
        for (int i = 0; i < 100; i++)
            rb.Add(i * 7);

        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            rb.Serialise(path);
            var loaded = RoaringBitmap.Deserialise(path);

            Assert.Equal(rb.Cardinality, loaded.Cardinality);
            for (int i = 0; i < 100; i++)
            {
                Assert.True(loaded.Contains(i * 7));
                Assert.False(loaded.Contains(i * 7 + 1));
            }
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies the Serialise: Round-trip Bitmap Container scenario.
    /// </summary>
    [Fact(DisplayName = "Serialise: Round-trip Bitmap Container")]
    public void Serialise_RoundTrip_BitmapContainer()
    {
        var rb = new RoaringBitmap();
        // Add >4096 values in same chunk → triggers bitmap container
        for (int i = 0; i < 5000; i++)
            rb.Add(i);

        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            rb.Serialise(path);
            var loaded = RoaringBitmap.Deserialise(path);

            Assert.Equal(5000, loaded.Cardinality);
            for (int i = 0; i < 5000; i++)
                Assert.True(loaded.Contains(i));
            Assert.False(loaded.Contains(5000));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies the Serialise: Round-trip Run Container scenario.
    /// </summary>
    [Fact(DisplayName = "Serialise: Round-trip Run Container")]
    public void Serialise_RoundTrip_RunContainer()
    {
        var rb = new RoaringBitmap();
        // Consecutive range → after Optimise, should use RunContainer
        for (int i = 1000; i < 2000; i++)
            rb.Add(i);
        rb.OptimiseRuns();

        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            rb.Serialise(path);
            var loaded = RoaringBitmap.Deserialise(path);

            Assert.Equal(1000, loaded.Cardinality);
            Assert.False(loaded.Contains(999));
            Assert.True(loaded.Contains(1000));
            Assert.True(loaded.Contains(1999));
            Assert.False(loaded.Contains(2000));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies the Serialise: Round-trip Multiple Chunks scenario.
    /// </summary>
    [Fact(DisplayName = "Serialise: Round-trip Multiple Chunks")]
    public void Serialise_RoundTrip_MultipleChunks()
    {
        var rb = new RoaringBitmap();
        // Spread across multiple 64K chunks
        rb.Add(0);
        rb.Add(65536);
        rb.Add(131072);
        rb.Add(200000);

        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            rb.Serialise(path);
            var loaded = RoaringBitmap.Deserialise(path);

            Assert.Equal(4, loaded.Cardinality);
            Assert.True(loaded.Contains(0));
            Assert.True(loaded.Contains(65536));
            Assert.True(loaded.Contains(131072));
            Assert.True(loaded.Contains(200000));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies the Serialise: Empty Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Serialise: Empty Round Trips")]
    public void Serialise_Empty_RoundTrips()
    {
        var rb = new RoaringBitmap();

        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            rb.Serialise(path);
            var loaded = RoaringBitmap.Deserialise(path);
            Assert.Equal(0, loaded.Cardinality);
            Assert.True(loaded.IsEmpty);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies the Deserialise: Rejects Corrupt CRC scenario.
    /// </summary>
    [Fact(DisplayName = "Deserialise: Rejects Corrupt CRC")]
    public void Deserialise_RejectsCorruptCrc()
    {
        var rb = new RoaringBitmap();
        for (int i = 0; i < 100; i++) rb.Add(i * 3);

        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            rb.Serialise(path);
            // Flip a single byte inside the payload to invalidate the CRC.
            var bytes = File.ReadAllBytes(path);
            bytes[10] ^= 0xFF;
            File.WriteAllBytes(path, bytes);

            Assert.Throws<InvalidDataException>(() => RoaringBitmap.Deserialise(path));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies the Deserialise: Rejects Truncated File scenario.
    /// </summary>
    [Fact(DisplayName = "Deserialise: Rejects Truncated File")]
    public void Deserialise_RejectsTruncatedFile()
    {
        var rb = new RoaringBitmap();
        for (int i = 0; i < 100; i++) rb.Add(i * 3);

        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            rb.Serialise(path);
            var bytes = File.ReadAllBytes(path);
            // Drop the trailing CRC and last few payload bytes.
            File.WriteAllBytes(path, bytes.AsSpan(0, bytes.Length - 8).ToArray());

            Assert.Throws<InvalidDataException>(() => RoaringBitmap.Deserialise(path));
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Verifies the Deserialise: Rejects Bad Magic scenario.
    /// </summary>
    [Fact(DisplayName = "Deserialise: Rejects Bad Magic")]
    public void Deserialise_RejectsBadMagic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"roaring_test_{Guid.NewGuid():N}.bin");
        try
        {
            File.WriteAllBytes(path, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });
            Assert.Throws<InvalidDataException>(() => RoaringBitmap.Deserialise(path));
        }
        finally { File.Delete(path); }
    }
}
