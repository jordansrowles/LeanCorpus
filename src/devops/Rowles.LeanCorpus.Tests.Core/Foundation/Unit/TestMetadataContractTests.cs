namespace Rowles.LeanCorpus.Tests.Core.Foundation.Unit;

[Category(TestCategory.Unit)]
[Area(TestArea.Foundation)]
public sealed class TestMetadataContractTests
{
    [Fact]
    public void CategoryAttribute_exposes_a_single_typed_category_trait()
    {
        var attribute = new CategoryAttribute(TestCategory.Unit);

        Assert.Equal(TestCategory.Unit, attribute.Category);
        Assert.Collection(
            attribute.GetTraits(),
            trait =>
            {
                Assert.Equal("Category", trait.Key);
                Assert.Equal("Unit", trait.Value);
            });
    }

    [Fact]
    public void AreaAttribute_exposes_a_typed_area_trait()
    {
        var attribute = new AreaAttribute(TestArea.Foundation);

        Assert.Equal(TestArea.Foundation, attribute.Area);
        Assert.Collection(
            attribute.GetTraits(),
            trait =>
            {
                Assert.Equal("Area", trait.Key);
                Assert.Equal("Foundation", trait.Value);
            });
    }
}
