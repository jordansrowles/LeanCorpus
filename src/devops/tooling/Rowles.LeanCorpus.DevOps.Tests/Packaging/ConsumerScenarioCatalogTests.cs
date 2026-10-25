using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class ConsumerScenarioCatalogTests
{
    [Fact]
    public void AllScopeCoversEveryShippingConsumerAcrossBothFrameworks()
    {
        var repositoryRoot = FindRepositoryRoot();
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, "scripts/devops/packaging/package-policy.json"), repositoryRoot);
        var scenarios = ConsumerScenarioCatalog.Create(policy, "all", repositoryRoot);

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
        var repositoryRoot = FindRepositoryRoot();
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, "scripts/devops/packaging/package-policy.json"), repositoryRoot);
        var scenarios = ConsumerScenarioCatalog.Create(policy, "compression", repositoryRoot);

        Assert.Equal(6, scenarios.Count);
        Assert.All(scenarios, static scenario => Assert.StartsWith("compression-", scenario.Name, StringComparison.Ordinal));
        Assert.Equal(3, scenarios.Select(static scenario => scenario.PackageId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(scenarios, static scenario => Assert.Equal(["LeanCorpus", scenario.PackageId], scenario.PackageIds));
    }

    [Fact]
    public void ConsumerFrameworkCasesFollowEachPolicyPackageTarget()
    {
        var repositoryRoot = FindRepositoryRoot();
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, "scripts/devops/packaging/package-policy.json"), repositoryRoot);
        var narrowedText = policy.Packages.Select(entry => entry.PackageId == "Rowles.Text"
            ? entry with { ExpectedFrameworks = ["net10.0"] }
            : entry).ToArray();

        var scenarios = ConsumerScenarioCatalog.Create(policy with { Packages = narrowedText }, "all", repositoryRoot);

        Assert.Equal("net10.0", Assert.Single(scenarios, static scenario => scenario.PackageId == "Rowles.Text").Framework);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts/devops/packaging/package-policy.json")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The LeanCorpus package policy was not found from the current test directory.");
    }
}
