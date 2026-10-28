using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class VectorFilePathsTests
{
    [Fact]
    public void Sanitise_AllSafeChars_ReturnsIdentical()
    {
        const string name = "field_name123";

        var result = VectorFilePaths.Sanitise(name);

        Assert.Equal(name, result);
    }

    [Fact]
    public void Sanitise_UnderscoreIsPreserved_NoHashAppended()
    {
        const string name = "my_field";

        var result = VectorFilePaths.Sanitise(name);

        Assert.Equal("my_field", result);
        Assert.DoesNotContain("_", result[result.LastIndexOf('d')..]);
    }

    [Theory]
    [InlineData("field@name", "field_name")]
    [InlineData("field name", "field_name")]
    [InlineData("field.name", "field_name")]
    [InlineData("field-name", "field_name")]
    public void Sanitise_UnsafeChar_ReplacesWithUnderscoreAndAppendsHash(string input, string expectedSafePrefix)
    {
        var result = VectorFilePaths.Sanitise(input);

        Assert.StartsWith(expectedSafePrefix + "_", result);
        // 16 hex chars for 8 bytes of SHA-256
        Assert.Matches(@"^[A-Za-z0-9_]+_[0-9A-F]{16}$", result);
    }

    [Fact]
    public void Sanitise_MixedSafeAndUnsafe_ReplacesAndAppendsHash()
    {
        const string input = "abc@def";

        var result = VectorFilePaths.Sanitise(input);

        Assert.StartsWith("abc_def_", result);
        Assert.Equal("abc_def".Length + 1 + 16, result.Length);
    }

    [Fact]
    public void Sanitise_LongName_OverTwoFiftySix_UsesHeapAllocation()
    {
        // Length > 256 forces the heap branch at line 28
        var name = new string('a', 257);

        var result = VectorFilePaths.Sanitise(name);

        // All safe chars, no hash suffix
        Assert.Equal(name, result);
    }

    [Fact]
    public void Sanitise_LongNameWithUnsafeChar_HeapAllocationAndHash()
    {
        var name = new string('a', 255) + "@";

        var result = VectorFilePaths.Sanitise(name);

        Assert.StartsWith(new string('a', 255) + "_", result);
        Assert.EndsWith("_" + result[^16..], result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Sanitise_NullOrEmpty_ThrowsArgumentException(string? input)
    {
        Assert.ThrowsAny<ArgumentException>(() => VectorFilePaths.Sanitise(input!));
    }
}
