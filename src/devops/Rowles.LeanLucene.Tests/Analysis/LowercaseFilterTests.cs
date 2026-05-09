using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Lowercase Filter.
/// </summary>
[Trait("Category", "Analysis")]
public class LowercaseFilterTests
{
    private readonly LowercaseFilter _filter = new();

    /// <summary>
    /// Verifies the Apply: Mixed Case Input Lowercases In Place scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Mixed Case Input Lowercases In Place")]
    public void Apply_MixedCaseInput_LowercasesInPlace()
    {
        var buffer = "Hello WORLD FoO".ToCharArray();

        _filter.Apply(buffer);

        Assert.Equal("hello world foo", new string(buffer));
    }

    /// <summary>
    /// Verifies the Apply: Already Lowercase Remains Unchanged scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Already Lowercase Remains Unchanged")]
    public void Apply_AlreadyLowercase_RemainsUnchanged()
    {
        var buffer = "abc".ToCharArray();

        _filter.Apply(buffer);

        Assert.Equal("abc", new string(buffer));
    }

    /// <summary>
    /// Verifies the Apply: Empty Buffer Does Not Throw scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Empty Buffer Does Not Throw")]
    public void Apply_EmptyBuffer_DoesNotThrow()
    {
        _filter.Apply(Span<char>.Empty);
    }
}
