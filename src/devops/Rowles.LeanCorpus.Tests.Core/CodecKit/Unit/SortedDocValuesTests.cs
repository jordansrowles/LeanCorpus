using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Contains unit tests for Sorted Doc Values.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
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

    [Fact(DisplayName = "Roundtrip: All Missing Values Do Not Create an Empty Term")]
    public void Roundtrip_AllMissing_ProducesNoTerms()
    {
        var path = Path.Combine(_dir, "all-missing.dvs");
        var fields = new Dictionary<string, string?[]> { ["tag"] = [null, null] };

        SortedDocValuesWriter.Write(path, fields, 2);

        var terms = SortedDocValuesReader.ReadTerms(path);
        var result = SortedDocValuesReader.Read(path);
        Assert.Empty(terms["tag"]);
        Assert.Equal(["", ""], result.Values["tag"]);
        Assert.DoesNotContain(0, result.Presence["tag"]!);
        Assert.DoesNotContain(1, result.Presence["tag"]!);
    }

    [Fact(DisplayName = "Roundtrip: Missing Values Do Not Pollute Non-Empty Terms")]
    public void Roundtrip_MissingAndNonEmpty_ListsOnlyPresentTerms()
    {
        var path = Path.Combine(_dir, "missing-and-value.dvs");
        var fields = new Dictionary<string, string?[]> { ["tag"] = [null, "alpha", null] };

        SortedDocValuesWriter.Write(path, fields, 3);

        var terms = SortedDocValuesReader.ReadTerms(path);
        var result = SortedDocValuesReader.Read(path);
        Assert.Equal(["alpha"], terms["tag"]);
        Assert.Equal(["", "alpha", ""], result.Values["tag"]);
        Assert.DoesNotContain(0, result.Presence["tag"]!);
        Assert.Contains(1, result.Presence["tag"]!);
        Assert.DoesNotContain(2, result.Presence["tag"]!);
    }

    [Fact(DisplayName = "Roundtrip: Explicit Empty String Remains a Term")]
    public void Roundtrip_ExplicitEmptyString_IsListedAsTerm()
    {
        var path = Path.Combine(_dir, "explicit-empty.dvs");
        var fields = new Dictionary<string, string?[]> { ["tag"] = [""] };

        SortedDocValuesWriter.Write(path, fields, 1);

        var terms = SortedDocValuesReader.ReadTerms(path);
        var result = SortedDocValuesReader.Read(path);
        Assert.Equal([""], terms["tag"]);
        Assert.Equal([""], result.Values["tag"]);
        Assert.Null(result.Presence["tag"]);
    }

    [Fact(DisplayName = "Roundtrip: Missing and Explicit Empty String Remain Distinct")]
    public void Roundtrip_MissingAndExplicitEmptyString_UsesPresence()
    {
        var path = Path.Combine(_dir, "missing-and-explicit-empty.dvs");
        var fields = new Dictionary<string, string?[]> { ["tag"] = [null, ""] };

        SortedDocValuesWriter.Write(path, fields, 2);

        var terms = SortedDocValuesReader.ReadTerms(path);
        var result = SortedDocValuesReader.Read(path);
        Assert.Equal([""], terms["tag"]);
        Assert.Equal(["", ""], result.Values["tag"]);
        Assert.DoesNotContain(0, result.Presence["tag"]!);
        Assert.Contains(1, result.Presence["tag"]!);
    }

    [Fact(DisplayName = "Read: Sorted DocValues v2 keeps its historical placeholder term")]
    public void Read_V2MissingAndNonEmpty_KeepsHistoricalTermsAndPresence()
    {
        var path = Path.Combine(_dir, "legacy-v2.dvs");
        WriteLegacyV2WithMissingValue(path);

        var terms = SortedDocValuesReader.ReadTerms(path);
        var result = SortedDocValuesReader.Read(path);

        Assert.Equal(["", "alpha"], terms["tag"]);
        Assert.Equal(["", "alpha"], result.Values["tag"]);
        Assert.DoesNotContain(0, result.Presence["tag"]!);
        Assert.Contains(1, result.Presence["tag"]!);
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

    private static void WriteLegacyV2WithMissingValue(string path)
    {
        var presence = new RoaringBitmap();
        presence.Add(1);
        using var bitmapStream = new MemoryStream();
        using (var bitmapWriter = new BinaryWriter(bitmapStream, Encoding.UTF8, leaveOpen: true))
            presence.Serialise(bitmapWriter);

        using var output = new IndexOutput(path);
        using var frame = CodecFileHeader.BeginStreamingWrite(output, version: 2);
        var body = frame.Output;
        body.WriteInt32(1);
        body.WriteString("tag");
        body.WriteInt32(checked((int)bitmapStream.Length));
        body.WriteBytes(bitmapStream.GetBuffer().AsSpan(0, checked((int)bitmapStream.Length)));
        body.WriteInt32(2);
        body.WriteInt32(2);
        body.WriteString("");
        body.WriteString("alpha");
        body.WriteByte(1);
        body.WriteByte(2);
    }
}
