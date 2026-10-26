using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Bootstrap;

public sealed class ManagedHostFingerprintTests
{
    [Fact]
    public void AcceptsCurrentRootApphostAndRejectsChangedManagedSource()
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(checkout.Root, "artifacts", "devops", "managed-host.json"));

        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, executable));
        File.AppendAllText(Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Program.cs"), "// source changed");

        Assert.Contains("stale", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("modify-template")]
    [InlineData("modify-source")]
    [InlineData("add-source")]
    [InlineData("delete-source")]
    [InlineData("rename-source")]
    public void FingerprintsManagedHostEmbeddedResourcesAndRejectsInventoryChanges(string mutation)
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(checkout.Root, "artifacts", "devops", "managed-host.json"));

        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, executable));

        var assets = Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Packaging", "Assets");
        var template = Path.Combine(assets, "Consumer.csproj.template");
        var source = Path.Combine(assets, "ConsumerSources", "core.cs.txt");
        switch (mutation)
        {
            case "modify-template":
                File.AppendAllText(template, " changed");
                break;
            case "modify-source":
                File.AppendAllText(source, " changed");
                break;
            case "add-source":
                File.WriteAllText(Path.Combine(Path.GetDirectoryName(source)!, "added.cs.txt"), "added consumer source");
                break;
            case "delete-source":
                File.Delete(source);
                break;
            case "rename-source":
                File.Move(source, Path.Combine(Path.GetDirectoryName(source)!, "renamed.cs.txt"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown embedded-resource mutation.");
        }

        Assert.Contains("stale", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IgnoresGeneratedArtifactDirectoriesWithinTheManagedHostTree()
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(checkout.Root, "artifacts", "devops", "managed-host.json"));

        var hostSource = Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps");
        foreach (var directoryName in new[] { "artifacts", "bin", "obj" })
        {
            var generatedDirectory = Path.Combine(hostSource, directoryName, "Nested");
            Directory.CreateDirectory(generatedDirectory);
            File.WriteAllText(Path.Combine(generatedDirectory, "Generated.cs"), "generated output");
        }

        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, executable));
    }

    [Fact]
    public void RejectsReparsePointUnderTheManagedHostSourceTree()
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(checkout.Root, "artifacts", "devops", "managed-host.json"));

        var hostSource = Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps");
        var linkedSourceDirectory = Path.Combine(checkout.Root, "linked source directory");
        Directory.CreateDirectory(linkedSourceDirectory);
        File.WriteAllText(Path.Combine(linkedSourceDirectory, "Linked.cs"), "linked source");
        CreateDirectoryLink(Path.Combine(hostSource, "LinkedSources"), linkedSourceDirectory);

        Assert.Contains("link or reparse point", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsReparsePointAtTheManagedHostSourceRoot()
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(checkout.Root, "artifacts", "devops", "managed-host.json"));

        var hostSource = Path.Combine(checkout.Root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps");
        var movedSource = Path.Combine(Path.GetTempPath(), "linked managed host source " + Guid.NewGuid().ToString("N"));
        Directory.Move(hostSource, movedSource);
        CreateDirectoryLink(hostSource, movedSource);
        try
        {
            Assert.Contains("link or reparse point", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(hostSource);
            Directory.Move(movedSource, hostSource);
        }
    }

    [Fact]
    public void RejectsChangedManagedConfiguration()
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(checkout.Root, "artifacts", "devops", "managed-host.json"));

        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, executable));
        File.AppendAllText(Path.Combine(checkout.Root, ".config", "devops", "test-suites.json"), " changed");

        Assert.Contains("stale", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RejectsApphostBytesThatDifferFromPromotionManifest()
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(checkout.Root, "artifacts", "devops", "managed-host.json"));
        File.AppendAllText(executable, "tampered");

        Assert.Contains("does not match", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidatesStagedApphostAgainstItsAdjacentManifest()
    {
        using var checkout = TemporaryCheckout.Create();
        var stage = Path.Combine(checkout.Root, "artifacts", "devops", "bootstrap-staging", Guid.NewGuid().ToString("D"));
        Directory.CreateDirectory(stage);
        var executable = Path.Combine(stage, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "staged host bytes");
        WriteManifest(checkout.Root, executable, Path.Combine(stage, "managed-host.json"));

        Assert.Null(ManagedHostFingerprint.Validate(checkout.Root, executable));
    }

    [Fact]
    public void RejectsRootApphostWithoutPromotionManifest()
    {
        using var checkout = TemporaryCheckout.Create();
        var executable = Path.Combine(checkout.Root, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
        File.WriteAllText(executable, "managed host bytes");

        Assert.Contains("promotion manifest", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteManifest(string repositoryRoot, string executable, string manifestPath)
    {
        var sourceFiles = GetSourceFiles(repositoryRoot).Select(relativePath => new
        {
            path = relativePath,
            sha256 = HashFile(Path.Combine(repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)))
        }).ToArray();
        var canonical = string.Concat(sourceFiles.Select(file => $"{file.path}\0{file.sha256}\n"));
        var sourceFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllBytes(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            executableSha256 = HashFile(executable),
            sourceFingerprint,
            sourceFiles
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static string[] GetSourceFiles(string root)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relative in new[]
        {
            "global.json", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
            "src/devops/Directory.Build.props", "src/devops/Directory.Packages.props"
        })
        {
            if (File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
            {
                paths.Add(relative);
            }
        }

        foreach (var directory in new[] { "src/devops/tooling/Rowles.LeanCorpus.DevOps", ".config/devops" })
        {
            var fullDirectory = Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDirectory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories))
            {
                var relativeFile = Path.GetRelativePath(fullDirectory, file);
                if (relativeFile.Split(Path.DirectorySeparatorChar).Any(IsTransientOutputDirectory))
                {
                    continue;
                }

                var isStandardSource = new[] { ".cs", ".csproj", ".json", ".props", ".targets", ".slnx" }
                    .Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase);
                var isManagedHostResource = directory == "src/devops/tooling/Rowles.LeanCorpus.DevOps" &&
                    (Path.GetExtension(file).Equals(".template", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetFileName(file).EndsWith(".cs.txt", StringComparison.OrdinalIgnoreCase));
                if (isStandardSource || isManagedHostResource)
                {
                    paths.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }

        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsTransientOutputDirectory(string name)
        => name.Equals("artifacts", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("obj", StringComparison.OrdinalIgnoreCase);

    private static void CreateDirectoryLink(string linkPath, string targetPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return;
        }

        var startInfo = new System.Diagnostics.ProcessStartInfo(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "/c", "mklink", "/J", linkPath, targetPath })
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe to create a test junction.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Creating a test junction failed with exit code {process.ExitCode}.{Environment.NewLine}{standardOutput}{Environment.NewLine}{standardError}");
        }
    }

    private static string HashFile(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root) => Root = root;

        public string Root { get; }

        public static TemporaryCheckout Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "DevOps fingerprint checkout " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, ".config", "devops"));
            foreach (var relative in new[]
            {
                "global.json", "src/devops/Directory.Build.props", "src/devops/Directory.Packages.props",
                "src/devops/tooling/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj",
                "src/devops/tooling/Rowles.LeanCorpus.DevOps/Program.cs", "src/devops/tooling/Rowles.LeanCorpus.DevOps/packages.lock.json", ".config/devops/test-suites.json"
            })
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fixture");
            }

            var hostProject = Path.Combine(root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Rowles.LeanCorpus.DevOps.csproj");
            File.WriteAllText(hostProject, """
                <Project>
                  <ItemGroup>
                    <EmbeddedResource Include="Packaging/Assets/Consumer.csproj.template" />
                    <EmbeddedResource Include="Packaging/Assets/Program.cs.template" />
                    <EmbeddedResource Include="Packaging/Assets/ConsumerSources/*.cs.txt" />
                  </ItemGroup>
                </Project>
                """);
            var assets = Path.Combine(root, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Packaging", "Assets");
            Directory.CreateDirectory(Path.Combine(assets, "ConsumerSources"));
            File.WriteAllText(Path.Combine(assets, "Consumer.csproj.template"), "consumer project template");
            File.WriteAllText(Path.Combine(assets, "Program.cs.template"), "consumer program template");
            File.WriteAllText(Path.Combine(assets, "ConsumerSources", "core.cs.txt"), "core consumer source");

            return new TemporaryCheckout(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
