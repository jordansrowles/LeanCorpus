using System.Xml.Linq;
using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageConsumerNuGetConfigTests
{
    [Fact]
    public void MapsPolicyPackagesToTheLocalGeneratedFeed()
    {
        var repositoryRoot = FindRepositoryRoot();
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, ".config/devops/package-policy.json"), repositoryRoot);
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "NuGet.Config");

        PackageConsumerNuGetConfig.Write(configPath, directory.Path, ["https://api.nuget.org/v3/index.json"], policy);

        var document = XDocument.Load(configPath);
        var localMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "local");
        var localPatterns = localMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")).ToArray();
        Assert.Equal(policy.Packages.Select(static entry => entry.PackageId).Order(StringComparer.Ordinal), localPatterns.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("LeanCorpus*", localPatterns);
        var remoteMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "feed0");
        Assert.Contains(remoteMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")), pattern => pattern == "*");
        Assert.DoesNotContain(remoteMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")),
            pattern => policy.Packages.Any(entry => entry.PackageId == pattern));
    }

    [Fact]
    public void MapsTheContractCorePackageToExternalFeedsForTheNegativeCompatibilityProbe()
    {
        var repositoryRoot = FindRepositoryRoot();
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, ".config/devops/package-policy.json"), repositoryRoot);
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "Negative.NuGet.Config");

        PackageConsumerNuGetConfig.Write(configPath, directory.Path, ["https://api.nuget.org/v3/index.json"], policy, allowExternalCore: true);

        var document = XDocument.Load(configPath);
        var localMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "local");
        var remoteMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "feed0");
        Assert.Equal(policy.Packages.Select(static entry => entry.PackageId).Order(StringComparer.Ordinal),
            localMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")).Order(StringComparer.Ordinal));
        var remotePatterns = remoteMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")).ToArray();
        Assert.Contains("*", remotePatterns);
        Assert.Contains(policy.CompressionContract.CorePackageId, remotePatterns);
        Assert.DoesNotContain(remotePatterns, pattern => policy.Packages.Any(entry => entry.PackageId == pattern &&
            entry.PackageId != policy.CompressionContract.CorePackageId));
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".config/devops/package-policy.json")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The package policy was not found from the current test directory.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "leancorpus-nuget-config-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
