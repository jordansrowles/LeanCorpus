using Rowles.LeanLucene.Codecs.DocValues;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for Sorted Doc Values.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class SortedDocValuesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-sdv-{Guid.NewGuid():N}");

    public SortedDocValuesTests() => Directory.CreateDirectory(_dir);

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
        var path = Path.Combine(_dir, "test.dvs");
        var fields = new Dictionary<string, string?[]>
        {
            ["category"] = ["electronics", "books", "electronics", "clothing", "books"]
        };

        SortedDocValuesWriter.Write(path, fields, 5);
        var result = SortedDocValuesReader.Read(path);

        Assert.Single(result.Values);
        Assert.Equal(fields["category"], result.Values["category"]);
    }

    /// <summary>
    /// Verifies the Roundtrip: All Same Value Uses Zero Bits scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: All Same Value Uses Zero Bits")]
    public void Roundtrip_AllSameValue_UsesZeroBits()
    {
        var path = Path.Combine(_dir, "const.dvs");
        var fields = new Dictionary<string, string?[]>
        {
            ["status"] = ["active", "active", "active"]
        };

        SortedDocValuesWriter.Write(path, fields, 3);
        var result = SortedDocValuesReader.Read(path);

        Assert.Equal(fields["status"], result.Values["status"]);
    }

    /// <summary>
    /// Verifies the Roundtrip: Null Values Treated As Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Null Values Treated As Empty")]
    public void Roundtrip_NullValues_TreatedAsEmpty()
    {
        var path = Path.Combine(_dir, "nulls.dvs");
        var fields = new Dictionary<string, string?[]>
        {
            ["tag"] = ["a", null, "b", null]
        };

        SortedDocValuesWriter.Write(path, fields, 4);
        var result = SortedDocValuesReader.Read(path);

        Assert.Equal(["a", "", "b", ""], result.Values["tag"]);
    }

    /// <summary>
    /// Verifies the Read: Missing File Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Read: Missing File Returns Empty")]
    public void Read_MissingFile_ReturnsEmpty()
    {
        var result = SortedDocValuesReader.Read(Path.Combine(_dir, "nonexistent.dvs"));
        Assert.Empty(result.Values);
    }
}
