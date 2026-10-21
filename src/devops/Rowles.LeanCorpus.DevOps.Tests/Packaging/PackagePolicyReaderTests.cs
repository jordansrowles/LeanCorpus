using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackagePolicyReaderTests
{
    [Fact]
    public void ReadsEveryShippingPackageFromTheRepositoryPolicy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var policy = PackagePolicyReader.Read(
            Path.Combine(repositoryRoot, "scripts/devops/packaging/package-policy.json"),
            repositoryRoot);

        Assert.Equal(13, policy.Packages.Count);
        Assert.Equal("LeanCorpus", policy.Packages[0].PackageId);
        Assert.Equal(["net10.0", "net11.0"], policy.Packages[0].ExpectedFrameworks);
        Assert.Contains(policy.Packages, package => package.PackageId == "LeanCorpus.SourceGen" && package.Kind == "analyzer");
    }

    [Fact]
    public void StrictBaselineRequiresAnApiCompatibilityBaseline()
    {
        const string policy = """
            {
              "schemaVersion": 1,
              "packages": [
                {
                  "packageId": "Example.Package",
                  "projectPath": "src/Example/Example.csproj",
                  "kind": "library",
                  "expectedFrameworks": ["net10.0"],
                  "enableApiCompatibility": false,
                  "strictBaseline": true,
                  "requireXmlDocs": true,
                  "requireSymbols": true,
                  "packageGroups": []
                }
              ]
            }
            """;

        var exception = Assert.Throws<InvalidDataException>(() => PackagePolicyReader.Parse(policy, "/repo"));

        Assert.Contains("strictBaseline", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "scripts/devops/packaging/package-policy.json")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The LeanCorpus package policy was not found from the current test directory.");
    }
}
