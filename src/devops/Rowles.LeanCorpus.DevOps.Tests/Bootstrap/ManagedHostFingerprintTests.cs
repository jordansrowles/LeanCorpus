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
        File.AppendAllText(Path.Combine(checkout.Root, "src", "devops", "Rowles.LeanCorpus.DevOps", "Program.cs"), "// source changed");

        Assert.Contains("stale", ManagedHostFingerprint.Validate(checkout.Root, executable), StringComparison.OrdinalIgnoreCase);
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

        foreach (var directory in new[] { "src/devops/Rowles.LeanCorpus.DevOps", ".config/devops" })
        {
            var fullDirectory = Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(fullDirectory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(fullDirectory, "*", SearchOption.AllDirectories))
            {
                if (new[] { ".cs", ".csproj", ".json", ".props", ".targets", ".slnx" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                {
                    paths.Add(Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'));
                }
            }
        }

        return paths.Order(StringComparer.Ordinal).ToArray();
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
            Directory.CreateDirectory(Path.Combine(root, "src", "devops", "Rowles.LeanCorpus.DevOps"));
            Directory.CreateDirectory(Path.Combine(root, ".config", "devops"));
            foreach (var relative in new[]
            {
                "global.json", "src/devops/Directory.Build.props", "src/devops/Directory.Packages.props",
                "src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj",
                "src/devops/Rowles.LeanCorpus.DevOps/Program.cs", "src/devops/Rowles.LeanCorpus.DevOps/packages.lock.json", ".config/devops/test-suites.json"
            })
            {
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fixture");
            }
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
