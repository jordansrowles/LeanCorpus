using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class ConsumerScenarioCatalogTests
{
    [Fact]
    public void AllScopeCoversEveryShippingConsumerAcrossBothFrameworks()
    {
        var scenarios = ConsumerScenarioCatalog.Create("all");

        Assert.Equal(26, scenarios.Count);
        Assert.Equal(new[] { "net10.0", "net11.0" }, scenarios.Select(static scenario => scenario.Framework).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(2, scenarios.Count(static scenario => scenario.RuntimeKind == "core"));
        Assert.Equal(6, scenarios.Count(static scenario => scenario.RuntimeKind == "compression"));
        Assert.Equal(2, scenarios.Count(static scenario => scenario.Kind == "sourcegen"));
        Assert.Contains(scenarios, static scenario => scenario.Kind == "server-local-launcher" && scenario.Sdk == "Microsoft.NET.Sdk");
        Assert.Contains(scenarios, static scenario => scenario.Kind == "orchard" && scenario.ExternalReferences.Contains("OrchardCore.Application.Cms.Targets"));
    }

    [Fact]
    public void CompressionScopeContainsOnlyCodecConsumersAcrossBothFrameworks()
    {
        var scenarios = ConsumerScenarioCatalog.Create("compression");

        Assert.Equal(6, scenarios.Count);
        Assert.All(scenarios, static scenario => Assert.StartsWith("compression-", scenario.Name, StringComparison.Ordinal));
        Assert.Equal(3, scenarios.Select(static scenario => scenario.PackageId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(scenarios, static scenario => Assert.Equal(["LeanCorpus", scenario.PackageId], scenario.PackageIds));
    }
}
