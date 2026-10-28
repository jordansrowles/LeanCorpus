using System.IO.Compression;
using System.Text.Json.Nodes;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Packaging;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Packaging;

public sealed class PackageBuildServiceTests
{
    [Fact]
    public async Task PacksPolicyProjectsAndWritesAHashManifest()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new PackageProcessRunner();
        Assert.Equal("https://example.test/repo", PackageProjectInspector.ReadMetadata(
            Path.Combine(checkout.Root, "src/LeanCorpus/LeanCorpus.csproj")).RepositoryUrl);
        var service = CreateService(checkout, process);

        var result = await service.PackAsync(new PackageBuildRequest { Checkout = checkout.Identity }, TestContext.Current.CancellationToken);

        Assert.True(result.CommandResult.Outcome == OutcomeKind.Success, result.CommandResult.Details ?? result.CommandResult.Message);
        Assert.NotNull(result.Manifest);
        Assert.Equal(RunState.Succeeded, result.Manifest.State);
        Assert.Equal(5, process.Requests.Count);
        Assert.All(process.Requests, AssertPackArguments);
        var packageManifest = Path.Combine(checkout.Root, "artifacts/package/release/manifest.json");
        Assert.True(File.Exists(packageManifest));
        using var json = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(packageManifest, TestContext.Current.CancellationToken));
        var packages = json.RootElement.GetProperty("packages").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "Example.Package", "LeanCorpus", "LeanCorpus.Compression.LZ4", "LeanCorpus.Compression.Snappy", "LeanCorpus.Compression.Zstandard" },
            packages.Select(package => package.GetProperty("packageId").GetString()).Order(StringComparer.Ordinal));
        Assert.All(packages, package => Assert.Equal(64, package.GetProperty("sha256").GetString()!.Length));
    }

    [Fact]
    public async Task RejectsARequiredPackageRemovedFromPolicyBeforeStartingPackProcesses()
    {
        using var checkout = TemporaryCheckout.Create();
        var process = new PackageProcessRunner();
        var policyPath = Path.Combine(checkout.Root, ".config/devops/package-policy.json");
        var policy = JsonNode.Parse(await File.ReadAllTextAsync(policyPath, TestContext.Current.CancellationToken))!;
        var packages = policy["packages"]!.AsArray();
        packages.RemoveAt(packages.IndexOf(packages.Single(item => item!["packageId"]!.GetValue<string>() == "LeanCorpus.Compression.LZ4")));
        await File.WriteAllTextAsync(policyPath, policy.ToJsonString(), TestContext.Current.CancellationToken);

        var result = await CreateService(checkout, process).PackAsync(
            new PackageBuildRequest { Checkout = checkout.Identity, CompressionOnly = true }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.Contains("Package policy is missing required release package 'LeanCorpus.Compression.LZ4'", result.CommandResult.Details, StringComparison.Ordinal);
        Assert.Empty(process.Requests);
    }

    private static PackageBuildService CreateService(TemporaryCheckout checkout, PackageProcessRunner process)
    {
        var paths = new ArtifactPaths(checkout.Root);
        return new PackageBuildService(paths, new ArtifactRunStore(paths, runCategory: "package"), process,
            new FakeSdkResolver(), new FakeProvenanceReader());
    }

    private static void AssertPackArguments(ProcessRequest request)
    {
        Assert.Equal("pack", request.Arguments[0]);
        Assert.Contains("--disable-build-servers", request.Arguments);
        Assert.Contains("-m:1", request.Arguments);
        Assert.Contains("-p:UseSharedCompilation=false", request.Arguments);
        Assert.Contains("--tl:off", request.Arguments);
    }

    private sealed class PackageProcessRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (request.Arguments.Count > 0 && request.Arguments[0] == "pack")
            {
                var output = request.Arguments[Array.IndexOf(request.Arguments.ToArray(), "--output") + 1];
                Directory.CreateDirectory(output);
                var metadata = PackageProjectInspector.ReadMetadata(request.Arguments[1]);
                using var archive = ZipFile.Open(Path.Combine(output, $"{metadata.PackageId}.{metadata.Version}.nupkg"), ZipArchiveMode.Create);
            var dependencyGroup = metadata.PackageId.StartsWith("LeanCorpus.Compression.", StringComparison.Ordinal)
                    ? "<dependencies><group targetFramework=\".NETCoreApp,Version=v10.0\"><dependency id=\"LeanCorpus\" version=\"[4.0.0,5.0.0)\" /></group></dependencies>"
                    : string.Empty;
                Add(archive, $"{metadata.PackageId}.nuspec", $"<package><metadata><id>{metadata.PackageId}</id><version>{metadata.Version}</version><repository url=\"{metadata.RepositoryUrl}\" />{dependencyGroup}</metadata></package>");
                Add(archive, $"lib/net10.0/{metadata.AssemblyName}.dll", "binary");
                Add(archive, $"lib/net10.0/{metadata.AssemblyName}.xml", "docs");
            }

            return Task.FromResult(new ProcessResult(OutcomeKind.Success, 0, [], [], 0, 0, TimeSpan.FromMilliseconds(2),
                request.StandardOutputLogPath, request.StandardErrorLogPath, null));
        }

        private static void Add(ZipArchive archive, string name, string content)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(content);
        }
    }

    private sealed class FakeSdkResolver : IDotNetSdkResolver
    {
        public Task<SdkResolution> ResolveAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new SdkResolution(OutcomeKind.Success, "11.0.100", "11.0.100", null, null));
    }

    private sealed class FakeProvenanceReader : IRepositoryProvenanceReader
    {
        public Task<GitProvenance> ReadAsync(CheckoutIdentity checkout, string runDirectory, CancellationToken cancellationToken)
            => Task.FromResult(new GitProvenance(OutcomeKind.Success, "abc123", "issue-97", true, null));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root)
        {
            Root = root;
            Identity = new CheckoutIdentity(root, Path.Combine(root, ".git"), CheckoutGitMarkerKind.Directory);
        }

        public string Root { get; }

        public CheckoutIdentity Identity { get; }

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "leancorpus-package-run-" + Guid.NewGuid().ToString("N"));
            var packaging = Path.Combine(root, ".config/devops");
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(packaging);
            var coreProject = Path.Combine(root, "src/LeanCorpus/LeanCorpus.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(coreProject)!);
            File.WriteAllText(coreProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>LeanCorpus</PackageId><Version>4.0.0</Version><RepositoryUrl>https://example.test/repo</RepositoryUrl></PropertyGroup></Project>");
            var coreRelativePath = "src/LeanCorpus/LeanCorpus.csproj";
            var coreDefinition = new
            {
                packageId = "LeanCorpus",
                projectPath = coreRelativePath,
                kind = "library",
                expectedFrameworks = new[] { "net10.0" },
                enableApiCompatibility = false,
                strictBaseline = false,
                requireXmlDocs = true,
                requireSymbols = false,
                packageGroups = new[] { "compression" },
                consumerScenario = "core"
            };
            var definitions = new[]
            {
                ("Example.Package", "src/Example/Example.csproj", false, "core"),
                ("LeanCorpus.Compression.LZ4", "src/LZ4/LZ4.csproj", true, "compression"),
                ("LeanCorpus.Compression.Snappy", "src/Snappy/Snappy.csproj", true, "compression"),
                ("LeanCorpus.Compression.Zstandard", "src/Zstandard/Zstandard.csproj", true, "compression")
            };
            var packages = new List<object> { coreDefinition };
            foreach (var (packageId, relativePath, compression, consumerScenario) in definitions)
            {
                var projectPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(projectPath)!);
                var assembly = Path.GetFileNameWithoutExtension(projectPath);
                var projectReference = compression ? "<ItemGroup><ProjectReference Include=\"../LeanCorpus/LeanCorpus.csproj\" /></ItemGroup>" : string.Empty;
                File.WriteAllText(projectPath, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>{packageId}</PackageId><Version>1.0.0</Version><AssemblyName>{assembly}</AssemblyName><RepositoryUrl>https://example.test/repo</RepositoryUrl></PropertyGroup>{projectReference}</Project>");
                packages.Add(new
                {
                    packageId,
                    projectPath = relativePath,
                    kind = "library",
                    expectedFrameworks = new[] { "net10.0" },
                    enableApiCompatibility = false,
                    strictBaseline = false,
                    requireXmlDocs = true,
                    requireSymbols = false,
                    packageGroups = compression ? new[] { "compression" } : Array.Empty<string>(),
                    consumerScenario
                });
            }
            File.WriteAllText(Path.Combine(packaging, "package-policy.json"), System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 2,
                releaseContract = new { version = 1, requiredPackageIds = packages.Select(item => item.GetType().GetProperty("packageId")!.GetValue(item)).ToArray() },
                compressionContract = new
                {
                    version = 1,
                    packageGroup = "compression",
                    corePackageId = "LeanCorpus",
                    requiredPackageIds = new[] { "LeanCorpus", "LeanCorpus.Compression.LZ4", "LeanCorpus.Compression.Snappy", "LeanCorpus.Compression.Zstandard" },
                    coreDependencyVersionRange = "[4.0.0,5.0.0)"
                },
                packages
            }));
            return new TemporaryCheckout(root);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
