using Rowles.LeanCorpus.DevOps.Packaging;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageConsumerProjectWriterTests
{
    [Fact]
    public void WritesSourceGeneratorConsumerFromAssetsOutsideTheCheckout()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var repository = TemporaryRepository.Create(repositoryRoot);
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, ".config/devops/package-policy.json"), repositoryRoot);
        var scenario = ConsumerScenarioCatalog.Create(policy, "all", repositoryRoot).First(item => item.Kind == "sourcegen");
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

    [Fact]
    public void WritesCompressionConsumerFromEmbeddedProgramTemplate()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var repository = TemporaryRepository.Create(repositoryRoot);
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, ".config/devops/package-policy.json"), repositoryRoot);
        var scenario = ConsumerScenarioCatalog.Create(policy, "compression", repositoryRoot).First();
        var packages = scenario.PackageIds.ToDictionary(static id => id, static id =>
            new PackageManifestPackage(id, "4.0.0", $"/packages/{id}.nupkg", $"{id}.nupkg", new string('a', 64)), StringComparer.Ordinal);

        var consumer = PackageConsumerProjectWriter.Write(repository.Root, Path.Combine(repository.Root, "consumer"), scenario,
            new PackageManifestIndex("abc123", packages));
        var program = File.ReadAllText(consumer.ProgramPath);

        Assert.DoesNotContain("CORE_ASSEMBLY_VERSION", program, StringComparison.Ordinal);
        Assert.DoesNotContain("OPTIONAL_ASSEMBLY_VERSION", program, StringComparison.Ordinal);
        Assert.DoesNotContain("REGISTRATION", program, StringComparison.Ordinal);
        Assert.DoesNotContain("POLICY", program, StringComparison.Ordinal);
        Assert.Contains(scenario.PackageId, File.ReadAllText(consumer.ProjectPath), StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPolicyScenarioCanBeMaterialisedFromEmbeddedAssetsOutsideTheCheckout()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var repository = TemporaryRepository.Create(repositoryRoot);
        var policy = PackagePolicyReader.Read(Path.Combine(repositoryRoot, ".config/devops/package-policy.json"), repositoryRoot);
        var scenarios = ConsumerScenarioCatalog.Create(policy, "all", repositoryRoot);
        var packages = policy.Packages.ToDictionary(static entry => entry.PackageId, static entry =>
            new PackageManifestPackage(entry.PackageId, "4.0.0", $"/packages/{entry.PackageId}.nupkg", $"{entry.PackageId}.nupkg", new string('a', 64)), StringComparer.Ordinal);
        var packageIndex = new PackageManifestIndex("abc123", packages);

        foreach (var scenario in scenarios)
        {
            var outputDirectory = Path.Combine(repository.Root, "generated", scenario.Name, scenario.Framework);
            var consumer = PackageConsumerProjectWriter.Write(repository.Root, outputDirectory, scenario, packageIndex);

            Assert.True(new FileInfo(consumer.ProjectPath).Length > 0, scenario.Name);
            Assert.True(new FileInfo(consumer.ProgramPath).Length > 0, scenario.Name);
            Assert.DoesNotContain("PACKAGE_REFERENCES", File.ReadAllText(consumer.ProjectPath), StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Environment.CurrentDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, ".config/devops/package-policy.json")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("The LeanCorpus package policy was not found from the current test directory.");
    }

    private sealed class TemporaryRepository : IDisposable
    {
        private TemporaryRepository(string root) => Root = root;

        public string Root { get; }

        public static TemporaryRepository Create(string repositoryRoot)
        {
            var root = Path.Combine(Path.GetTempPath(), "leancorpus-consumer-project-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.Copy(Path.Combine(repositoryRoot, "Directory.Packages.props"), Path.Combine(root, "Directory.Packages.props"));
            return new TemporaryRepository(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
