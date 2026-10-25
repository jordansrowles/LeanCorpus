using System.Text.Json.Nodes;
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

        Assert.Equal(2, policy.SchemaVersion);
        Assert.Equal(13, policy.Packages.Count);
        Assert.Equal("LeanCorpus", policy.Packages[0].PackageId);
        Assert.Equal(["net10.0", "net11.0"], policy.Packages[0].ExpectedFrameworks);
        Assert.Contains(policy.Packages, package => package.PackageId == "LeanCorpus.SourceGen" && package.Kind == "analyzer");
        Assert.Equal(13, policy.ReleaseContract.RequiredPackageIds.Count);
        Assert.Equal(4, policy.CompressionContract.RequiredPackageIds.Count);
        Assert.Equal("[4.0.0,5.0.0)", policy.CompressionContract.CoreDependencyVersionRange);
    }

    [Fact]
    public void StrictBaselineRequiresAnApiCompatibilityBaseline()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = ReadPolicyDocument(repositoryRoot);
        var text = document["packages"]!.AsArray().Single(item => item!["packageId"]!.GetValue<string>() == "Rowles.Text");
        text!["enableApiCompatibility"] = false;
        text["strictBaseline"] = true;

        var exception = Assert.Throws<InvalidDataException>(() => PackagePolicyReader.Parse(document.ToJsonString(), repositoryRoot));

        Assert.Contains("strictBaseline", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsARequiredReleasePackageRemovedFromTheCatalogue()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = ReadPolicyDocument(repositoryRoot);
        var packages = document["packages"]!.AsArray();
        packages.RemoveAt(packages.IndexOf(packages.Single(item => item!["packageId"]!.GetValue<string>() == "LeanCorpus.Compression.LZ4")));

        var exception = Assert.Throws<InvalidDataException>(() => PackagePolicyReader.Parse(document.ToJsonString(), repositoryRoot));

        Assert.Contains("Package policy is missing required release package 'LeanCorpus.Compression.LZ4'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCompressionGroupMembershipThatDiffersFromTheVersionedContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = ReadPolicyDocument(repositoryRoot);
        var codec = document["packages"]!.AsArray().Single(item => item!["packageId"]!.GetValue<string>() == "LeanCorpus.Compression.LZ4");
        codec!["packageGroups"] = new JsonArray();

        var exception = Assert.Throws<InvalidDataException>(() => PackagePolicyReader.Parse(document.ToJsonString(), repositoryRoot));

        Assert.Contains("compression group membership does not match its contract", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCompressionRangeThatDoesNotCoverTheCurrentCoreMajorVersion()
    {
        var repositoryRoot = FindRepositoryRoot();
        var document = ReadPolicyDocument(repositoryRoot);
        document["compressionContract"]!["coreDependencyVersionRange"] = "[3.0.0,4.0.0)";

        var exception = Assert.Throws<InvalidDataException>(() => PackagePolicyReader.Parse(document.ToJsonString(), repositoryRoot));

        Assert.Contains("does not cover the current Core package version", exception.Message, StringComparison.Ordinal);
    }

    private static JsonObject ReadPolicyDocument(string repositoryRoot)
        => JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "scripts/devops/packaging/package-policy.json")))!.AsObject();

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
