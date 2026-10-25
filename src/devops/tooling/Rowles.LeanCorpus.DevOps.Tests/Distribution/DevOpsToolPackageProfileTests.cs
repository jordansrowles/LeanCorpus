using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Distribution;

public sealed class DevOpsToolPackageProfileTests
{
    [Fact]
    public async Task ToolProfileIsPortableAndSeparateFromRepositoryApphost()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var runDirectory = Path.Combine(Path.GetTempPath(), "DevOps tool package profile " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runDirectory);

        try
        {
            var toolProperties = await ReadPropertiesAsync(checkout.RepositoryRoot, runDirectory, "Tool");
            Assert.Equal("devops", toolProperties["AssemblyName"]);
            Assert.Equal("net10.0", toolProperties["TargetFramework"]);
            Assert.Equal("true", toolProperties["IsPackable"]);
            Assert.Equal("true", toolProperties["PackAsTool"]);
            Assert.Equal("Rowles.LeanCorpus.DevOps", toolProperties["PackageId"]);
            Assert.Equal("1.0.0", toolProperties["PackageVersion"]);
            Assert.Equal("Jordan Rowles", toolProperties["Authors"]);
            Assert.Equal("Managed command-line tooling for building, testing, documenting, validating and packaging LeanCorpus.", toolProperties["Description"]);
            Assert.Equal("leancorpus;devops;dotnet-tool", toolProperties["PackageTags"]);
            Assert.Equal("README.md", toolProperties["PackageReadmeFile"]);
            Assert.Equal("LICENCE", toolProperties["PackageLicenseFile"]);
            Assert.Equal("https://github.com/jordansrowles/LeanCorpus", toolProperties["RepositoryUrl"]);
            Assert.Equal("git", toolProperties["RepositoryType"]);
            Assert.Equal("devops", toolProperties["ToolCommandName"]);
            Assert.Equal("false", toolProperties["UseAppHost"]);
            Assert.Equal("false", toolProperties["PublishSingleFile"]);
            Assert.Equal("false", toolProperties["SelfContained"]);
            Assert.Equal("false", toolProperties["PublishAot"]);
            Assert.Equal("false", toolProperties["CreateRidSpecificToolPackages"]);
            Assert.Equal("1.0.0", toolProperties["Version"]);
            Assert.Equal("1.0.0.0", toolProperties["AssemblyVersion"]);
            Assert.Equal("1.0.0.0", toolProperties["FileVersion"]);
            Assert.Equal("packages.tool.lock.json", Path.GetFileName(toolProperties["NuGetLockFilePath"]));
            Assert.Equal("true", toolProperties["RestorePackagesWithLockFile"]);
            Assert.Equal(string.Empty, toolProperties["RuntimeIdentifier"]);
            Assert.Equal(string.Empty, toolProperties["RuntimeIdentifiers"]);
            Assert.Equal(string.Empty, toolProperties["ToolPackageRuntimeIdentifiers"]);

            var archiveProperties = await ReadPropertiesAsync(checkout.RepositoryRoot, runDirectory, "ReleaseArchive");
            Assert.Equal("devops", archiveProperties["AssemblyName"]);
            Assert.Equal("net10.0", archiveProperties["TargetFramework"]);
            Assert.Equal("false", archiveProperties["IsPackable"]);
            Assert.Equal("true", archiveProperties["UseAppHost"]);
            Assert.Equal("packages.distribution.lock.json", Path.GetFileName(archiveProperties["NuGetLockFilePath"]));
            Assert.Equal("true", archiveProperties["RestorePackagesWithLockFile"]);

            var repositoryProperties = await ReadPropertiesAsync(checkout.RepositoryRoot, runDirectory, profile: null);
            Assert.Equal("leancorpus-devops", repositoryProperties["AssemblyName"]);
            Assert.Equal("1.0.0", repositoryProperties["Version"]);
            Assert.Equal("1.0.0", repositoryProperties["PackageVersion"]);
            Assert.Equal("1.0.0.0", repositoryProperties["AssemblyVersion"]);
            Assert.Equal("1.0.0.0", repositoryProperties["FileVersion"]);
            Assert.Equal("false", repositoryProperties["IsPackable"]);
            Assert.Equal("true", repositoryProperties["UseAppHost"]);
            Assert.Equal("win-x64;win-arm64;linux-x64;linux-arm64;osx-x64;osx-arm64", repositoryProperties["RuntimeIdentifiers"]);
        }
        finally
        {
            if (Directory.Exists(runDirectory))
            {
                Directory.Delete(runDirectory, recursive: true);
            }
        }
    }

    private static async Task<Dictionary<string, string>> ReadPropertiesAsync(string repositoryRoot, string runDirectory, string? profile)
    {
        var projectPath = Path.Combine(repositoryRoot, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Rowles.LeanCorpus.DevOps.csproj");
        var properties = string.Join(',',
            "AssemblyName", "TargetFramework", "IsPackable", "PackAsTool", "PackageId", "PackageVersion", "Version", "AssemblyVersion", "FileVersion", "Authors", "Description",
            "PackageTags", "PackageReadmeFile", "PackageLicenseFile", "RepositoryUrl", "RepositoryType", "ToolCommandName",
            "UseAppHost", "PublishSingleFile", "SelfContained", "PublishAot", "CreateRidSpecificToolPackages", "NuGetLockFilePath",
            "RestorePackagesWithLockFile",
            "RuntimeIdentifier", "RuntimeIdentifiers", "ToolPackageRuntimeIdentifiers");
        var arguments = new List<string>
        {
            "msbuild", projectPath,
            $"-getProperty:{properties}",
            "-p:Configuration=Release"
        };
        if (profile is not null)
        {
            arguments.Add($"-p:DevOpsDistributionProfile={profile}");
        }

        var result = await new ProcessRunner().RunAsync(new ProcessRequest
        {
            ExecutablePath = "dotnet",
            Arguments = arguments,
            WorkingDirectory = repositoryRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, $"{profile ?? "repository"}.stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, $"{profile ?? "repository"}.stderr.log"),
            Timeout = TimeSpan.FromMinutes(1)
        }, TestContext.Current.CancellationToken);

        Assert.Equal(Rowles.LeanCorpus.DevOps.Terminal.OutcomeKind.Success, result.Outcome);
        using var document = JsonDocument.Parse(string.Join(Environment.NewLine, result.StandardOutputTail));
        var propertyObject = document.RootElement.GetProperty("Properties");
        return propertyObject.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }
}
