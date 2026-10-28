using Rowles.LeanCorpus.Index.Format;

namespace Rowles.LeanCorpus.Tests.Core.Index;
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class IndexFormatInspectionOptionsTests
{
    [Fact]
    public void IncludeChecksums_Default_IsFalse()
    {
        var options = new IndexFormatInspectionOptions();

        Assert.False(options.IncludeChecksums);
    }

    [Fact]
    public void IncludeChecksums_SetTrue_RetainsValue()
    {
        var options = new IndexFormatInspectionOptions { IncludeChecksums = true };

        Assert.True(options.IncludeChecksums);
    }

    [Fact]
    public void IncludeChecksums_SetFalseExplicitly_RetainsValue()
    {
        var options = new IndexFormatInspectionOptions { IncludeChecksums = false };

        Assert.False(options.IncludeChecksums);
    }

    [Fact]
    public void IncludeChecksums_IsIndependentOfOtherProperties()
    {
        var options = new IndexFormatInspectionOptions
        {
            IncludeOptionalSidecars = false,
            IncludeFileSizes = false,
            IncludeChecksums = true
        };

        Assert.True(options.IncludeChecksums);
        Assert.False(options.IncludeOptionalSidecars);
        Assert.False(options.IncludeFileSizes);
    }
}
