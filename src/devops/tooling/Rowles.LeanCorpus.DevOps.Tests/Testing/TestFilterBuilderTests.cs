using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class TestFilterBuilderTests
{
    [Fact]
    public void CombinesAffectedAreasAndTraitsWithOrWithinEachTrait()
    {
        var filter = TestFilterBuilder.Build(
            affectedAreas: ["Search", "Index"],
            areas: ["Store", "Search"],
            categories: ["Integration", "Unit"],
            techniques: ["Merge"],
            filter: "FullyQualifiedName~Parser",
            defaultFilter: "FullyQualifiedName!~ProviderPerformanceTests.");

        Assert.Equal(
            "((Area=Index|Area=Search)&(Area=Search|Area=Store)&(Category=Integration|Category=Unit)&(Technique=Merge)&FullyQualifiedName~Parser)&(FullyQualifiedName!~ProviderPerformanceTests.)",
            filter);
    }

    [Fact]
    public void NormalisesTraitValuesAndLeavesEmptyFiltersEmpty()
    {
        Assert.Equal("(Category=Unit)", TestFilterBuilder.Build(categories: [" Unit ", "Unit", ""]));
        Assert.Equal(string.Empty, TestFilterBuilder.Build());
    }
}
