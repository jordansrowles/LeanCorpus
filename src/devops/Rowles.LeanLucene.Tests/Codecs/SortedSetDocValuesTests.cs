using Rowles.LeanLucene.Codecs.DocValues;

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// Contains unit tests for sorted-set DocValues.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class SortedSetDocValuesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-dss-{Guid.NewGuid():N}");

    public SortedSetDocValuesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Verifies repeated values round-trip as distinct sorted values per document.
    /// </summary>
    [Fact(DisplayName = "Roundtrip: Repeated Values Are Distinct And Sorted")]
    public void Roundtrip_RepeatedValues_AreDistinctAndSorted()
    {
        var path = Path.Combine(_dir, "tags.dss");
        IReadOnlyList<string>?[] tags =
        [
            ["bravo", "alpha", "alpha"],
            null,
            ["charlie", "alpha"]
        ];
        var fields = new Dictionary<string, IReadOnlyList<string>?[]>
        {
            ["tag"] = tags
        };

        SortedSetDocValuesWriter.Write(path, fields, 3);
        var result = SortedSetDocValuesReader.Read(path);

        Assert.Equal(["alpha", "bravo"], result["tag"][0]);
        Assert.Empty(result["tag"][1]);
        Assert.Equal(["alpha", "charlie"], result["tag"][2]);
    }

    /// <summary>
    /// Verifies missing optional sidecar files are treated as empty.
    /// </summary>
    [Fact(DisplayName = "Read: Missing File Returns Empty")]
    public void Read_MissingFile_ReturnsEmpty()
    {
        var result = SortedSetDocValuesReader.Read(Path.Combine(_dir, "missing.dss"));
        Assert.Empty(result);
    }
}
