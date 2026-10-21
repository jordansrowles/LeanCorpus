using System.Xml.Linq;
using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageConsumerNuGetConfigTests
{
    [Fact]
    public void MapsLeanCorpusPackagesToTheLocalGeneratedFeed()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "NuGet.Config");

        PackageConsumerNuGetConfig.Write(configPath, directory.Path, ["https://api.nuget.org/v3/index.json"]);

        var document = XDocument.Load(configPath);
        var localMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "local");
        Assert.Contains(localMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")), pattern => pattern == "LeanCorpus*");
        var remoteMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "feed0");
        Assert.Contains(remoteMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")), pattern => pattern == "*");
        Assert.DoesNotContain(remoteMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")), pattern => pattern == "LeanCorpus");
    }

    [Fact]
    public void AllowsOnlyTheCorePackageFromExternalFeedsForTheNegativeCompressionProbe()
    {
        using var directory = new TemporaryDirectory();
        var configPath = Path.Combine(directory.Path, "Negative.NuGet.Config");

        PackageConsumerNuGetConfig.Write(configPath, directory.Path, ["https://api.nuget.org/v3/index.json"], allowExternalLeanCorpus: true);

        var document = XDocument.Load(configPath);
        var localMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "local");
        var remoteMapping = document.Descendants("packageSource").Single(element => (string?)element.Attribute("key") == "feed0");
        Assert.Contains(localMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")), pattern => pattern == "LeanCorpus.Compression.*");
        Assert.Contains(remoteMapping.Elements("package").Select(element => (string?)element.Attribute("pattern")), pattern => pattern == "LeanCorpus");
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
