using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

/// <summary>
/// Contains unit tests for Numeric Doc Values.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class NumericDocValuesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-ndv-{Guid.NewGuid():N}");

    public NumericDocValuesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Verifies the Roundtrip: Single Field Preserves Values scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Single Field Preserves Values")]
    public void Roundtrip_SingleField_PreservesValues()
    {
        var path = Path.Combine(_dir, "test.dvn");
        var fields = new Dictionary<string, double[]>
        {
            ["price"] = [1.99, 2.50, 3.75, 0.0, 100.0]
        };

        NumericDocValuesWriter.Write(path, fields, 5);
        var result = NumericDocValuesReader.Read(path);

        Assert.Single(result.Values);
        Assert.True(result.Values.ContainsKey("price"));
        Assert.Equal(fields["price"], result.Values["price"]);
    }

    /// <summary>
    /// Verifies the Roundtrip: Multiple Fields Preserves All scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Multiple Fields Preserves All")]
    public void Roundtrip_MultipleFields_PreservesAll()
    {
        var path = Path.Combine(_dir, "multi.dvn");
        var fields = new Dictionary<string, double[]>
        {
            ["price"] = [10.0, 20.0, 30.0],
            ["rating"] = [4.5, 3.2, 5.0]
        };

        NumericDocValuesWriter.Write(path, fields, 3);
        var result = NumericDocValuesReader.Read(path);

        Assert.Equal(2, result.Values.Count);
        Assert.Equal(fields["price"], result.Values["price"]);
        Assert.Equal(fields["rating"], result.Values["rating"]);
    }

    /// <summary>
    /// Verifies the Roundtrip: All Same Value Uses Zero Bits scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: All Same Value Uses Zero Bits")]
    public void Roundtrip_AllSameValue_UsesZeroBits()
    {
        var path = Path.Combine(_dir, "const.dvn");
        var fields = new Dictionary<string, double[]>
        {
            ["score"] = [42.0, 42.0, 42.0, 42.0]
        };

        NumericDocValuesWriter.Write(path, fields, 4);
        var result = NumericDocValuesReader.Read(path);

        Assert.Equal(fields["score"], result.Values["score"]);
    }

    /// <summary>
    /// Verifies the Read: Missing File Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Read: Missing File Returns Empty")]
    public void Read_MissingFile_ReturnsEmpty()
    {
        var result = NumericDocValuesReader.Read(Path.Combine(_dir, "nonexistent.dvn"));
        Assert.Empty(result.Values);
    }

    /// <summary>
    /// Verifies the Roundtrip: Mixed Sign Bit Patterns Preserves Values scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Mixed Sign Bit Patterns Preserves Values")]
    public void Roundtrip_MixedSignBitPatterns_PreservesValues()
    {
        // Negative and positive doubles produce bit patterns whose long subtraction
        // overflows. The writer must subtract in ulong space.
        var path = Path.Combine(_dir, "mixed-sign.dvn");
        var values = new[] { -1.0, 1.0, -0.5, 0.5, double.MinValue, double.MaxValue, 0.0 };
        var fields = new Dictionary<string, double[]> { ["v"] = values };

        NumericDocValuesWriter.Write(path, fields, values.Length);
        var result = NumericDocValuesReader.Read(path);

        Assert.Equal(values, result.Values["v"]);
    }

    /// <summary>
    /// Verifies the Roundtrip: Negative Only Preserves Values scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Negative Only Preserves Values")]
    public void Roundtrip_NegativeOnly_PreservesValues()
    {
        var path = Path.Combine(_dir, "neg.dvn");
        var values = new[] { -100.0, -50.5, -1e9, -1e-9 };
        var fields = new Dictionary<string, double[]> { ["v"] = values };

        NumericDocValuesWriter.Write(path, fields, values.Length);
        var result = NumericDocValuesReader.Read(path);

        Assert.Equal(values, result.Values["v"]);
    }

    /// <summary>
    /// Verifies that a bits-per-value header larger than 64 is rejected.
    /// </summary>
    [Fact(DisplayName = "Read: BitsPerValue Above 64 Throws")]
    public void Read_BitsPerValueAbove64_Throws()
    {
        const string fieldName = "a";
        var path = Path.Combine(_dir, "bad-bits.dvn");
        NumericDocValuesWriter.Write(path, new Dictionary<string, double[]> { [fieldName] = [1.0] }, 1);

        long offset = BitsPerValueByteOffset(path, fieldName);
        OverwriteByte(path, offset, 65);

        Assert.Throws<InvalidDataException>(() => NumericDocValuesReader.Read(path));
    }

    /// <summary>
    /// Verifies that a field declaring more packed bytes than the file contains is rejected.
    /// </summary>
    [Fact(DisplayName = "Read: Truncated Packed Data Throws")]
    public void Read_TruncatedPackedData_Throws()
    {
        const string fieldName = "a";
        var path = Path.Combine(_dir, "trunc.dvn");
        NumericDocValuesWriter.Write(path, new Dictionary<string, double[]> { [fieldName] = [1.0, 2.0, 3.0] }, 3);

        long offset = BitsPerValueByteOffset(path, fieldName) + 1; // first packed byte
        var bytes = File.ReadAllBytes(path);
        File.WriteAllBytes(path, bytes.AsSpan(0, (int)offset).ToArray());

        Assert.Throws<InvalidDataException>(() => NumericDocValuesReader.Read(path));
    }

    private static long BitsPerValueByteOffset(string path, string fieldName)
    {
        int nameLen = System.Text.Encoding.UTF8.GetByteCount(fieldName);
        long bodyOffset = ComputeBodyOffset(path);
        // body: fieldCount(4) + nameLen varint(1) + name bytes + presenceCount(4) + docCount(4) + min(8) + bitsPerValue(1)
        return bodyOffset + 4 + 1 + nameLen + 4 + 4 + 8;
    }

    private static long ComputeBodyOffset(string path)
    {
        var bytes = File.ReadAllBytes(path);
        long offset = 1; // skip version byte
        while (offset < bytes.Length)
        {
            if ((bytes[offset] & 0x80) == 0)
            {
                offset++;
                break;
            }

            offset++;
        }

        return offset;
    }

    private static void OverwriteByte(string path, long offset, byte value)
    {
        var bytes = File.ReadAllBytes(path);
        bytes[offset] = value;
        File.WriteAllBytes(path, bytes);
    }
}
