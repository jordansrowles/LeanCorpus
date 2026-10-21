using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageConsumerProjectWriterTests
{
    [Fact]
    public void SourceGeneratorConsumerUsesTheExactPrivateAnalyzerPackageReference()
    {
        using var repository = TemporaryRepository.Create();
        var scenario = ConsumerScenarioCatalog.Create("all").First(item => item.Kind == "sourcegen");
        var packages = new Dictionary<string, PackageManifestPackage>(StringComparer.OrdinalIgnoreCase)
        {
            ["LeanCorpus"] = new("LeanCorpus", "4.0.0", "/packages/LeanCorpus.4.0.0.nupkg", "LeanCorpus.4.0.0.nupkg", new string('a', 64)),
            ["LeanCorpus.SourceGen"] = new("LeanCorpus.SourceGen", "1.2.0", "/packages/LeanCorpus.SourceGen.1.2.0.nupkg", "LeanCorpus.SourceGen.1.2.0.nupkg", new string('b', 64))
        };

        var consumer = PackageConsumerProjectWriter.Write(repository.Root, Path.Combine(repository.Root, "consumer"), scenario,
            new PackageManifestIndex("abc123", packages));
        var project = File.ReadAllText(consumer.ProjectPath);

        Assert.Contains("OutputItemType=\"Analyzer\"", project, StringComparison.Ordinal);
        Assert.Contains("ReferenceOutputAssembly=\"false\"", project, StringComparison.Ordinal);
        Assert.Contains("PrivateAssets=\"all\"", project, StringComparison.Ordinal);
        Assert.Contains("Version=\"[1.2.0]\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("<ProjectReference", project, StringComparison.Ordinal);
        Assert.Contains("SOURCEGEN", File.ReadAllText(consumer.ProgramPath), StringComparison.Ordinal);
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string root) => Root = root;

        public string Root { get; }

        public static TemporaryRepository Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "leancorpus-consumer-project-" + Guid.NewGuid().ToString("N"));
            var packaging = Path.Combine(root, "scripts/devops/packaging");
            var sources = Path.Combine(packaging, "consumer-sources");
            Directory.CreateDirectory(sources);
            File.WriteAllText(Path.Combine(packaging, "Consumer.csproj.template"), """
                <Project>
                  <PropertyGroup><ImportDirectoryBuildProps>false</ImportDirectoryBuildProps><TargetFramework>FRAMEWORK</TargetFramework>EXTRA_PROPERTIES</PropertyGroup>
                  <ItemGroup>PACKAGE_REFERENCES</ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(sources, "sourcegen.cs"), "Console.WriteLine(\"SOURCEGEN Product\");");
            return new TemporaryRepository(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
