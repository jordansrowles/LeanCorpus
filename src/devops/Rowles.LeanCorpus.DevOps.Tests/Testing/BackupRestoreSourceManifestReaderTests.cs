using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class BackupRestoreSourceManifestReaderTests
{
    [Fact]
    public async Task ValidatesProvidedFilesAndComputesOrderIndependentFingerprint()
    {
        using var checkout = new TemporaryCheckout();
        Directory.CreateDirectory(Path.Combine(checkout.Root, "src"));
        await File.WriteAllTextAsync(Path.Combine(checkout.Root, "src", "a.txt"), "alpha",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(Path.Combine(checkout.Root, "src", "b.txt"), "beta",
            TestContext.Current.CancellationToken);
        var a = Hash("alpha");
        var b = Hash("beta");
        var manifestPath = Path.Combine(checkout.Root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            commit = new string('a', 40),
            files = new[]
            {
                new { path = "src/b.txt", sha256 = b },
                new { path = "src/a.txt", sha256 = a }
            }
        }), TestContext.Current.CancellationToken);

        var reader = new BackupRestoreSourceManifestReader(new ProcessRunner());
        var result = await reader.ReadAsync(checkout.Root, new string('a', 40), manifestPath,
            checkout.Root, TestContext.Current.CancellationToken);

        Assert.Equal(new string('a', 40), result.Commit);
        Assert.Equal(2, result.Files.Count);
        Assert.Equal(Fingerprint(("src/a.txt", a), ("src/b.txt", b)), result.Fingerprint);
    }

    [Theory]
    [InlineData("../outside.txt", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("src/a.txt", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")]
    public async Task RejectsUnsafePathsAndChangedSourceFiles(string path, string hash)
    {
        using var checkout = new TemporaryCheckout();
        Directory.CreateDirectory(Path.Combine(checkout.Root, "src"));
        await File.WriteAllTextAsync(Path.Combine(checkout.Root, "src", "a.txt"), "alpha",
            TestContext.Current.CancellationToken);
        var manifestPath = Path.Combine(checkout.Root, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
        {
            commit = new string('a', 40), files = new[] { new { path, sha256 = hash } }
        }), TestContext.Current.CancellationToken);

        var reader = new BackupRestoreSourceManifestReader(new ProcessRunner());
        await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(checkout.Root,
            new string('a', 40), manifestPath, checkout.Root, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RejectsManifestSymlinksThatLeaveTheCheckout()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var checkout = new TemporaryCheckout();
        var outsidePath = Path.Combine(Path.GetTempPath(), "LeanCorpus source outside " + Guid.NewGuid().ToString("N"));
        try
        {
            await File.WriteAllTextAsync(outsidePath, "outside source", TestContext.Current.CancellationToken);
            File.CreateSymbolicLink(Path.Combine(checkout.Root, "linked.txt"), outsidePath);
            var manifestPath = Path.Combine(checkout.Root, "manifest.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(new
            {
                commit = new string('a', 40),
                files = new[] { new { path = "linked.txt", sha256 = Hash("outside source") } }
            }), TestContext.Current.CancellationToken);

            var reader = new BackupRestoreSourceManifestReader(new ProcessRunner());
            await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync(checkout.Root,
                new string('a', 40), manifestPath, checkout.Root, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (File.Exists(outsidePath))
            {
                File.Delete(outsidePath);
            }
        }
    }

    [Fact(Explicit = true)]
    public async Task GitInventoryPreservesWhitespaceAndNewlinePaths()
    {
        using var checkout = new TemporaryCheckout();
        var oddName = "source with space\tand\nnewline.txt";
        await File.WriteAllTextAsync(Path.Combine(checkout.Root, oddName), "tracked source",
            TestContext.Current.CancellationToken);
        var git = new ProcessRunner();
        var gitRequest = CheckoutGit(checkout.Root, "init");
        ProcessResult init;
        try
        {
            init = await git.RunAsync(gitRequest, TestContext.Current.CancellationToken);
        }
        finally
        {
            var logRoot = Path.GetDirectoryName(gitRequest.StandardOutputLogPath)!;
            if (Directory.Exists(logRoot))
            {
                Directory.Delete(logRoot, recursive: true);
            }
        }
        Assert.Equal(0, init.RawExitCode);
        var reader = new BackupRestoreSourceManifestReader(git);

        var result = await reader.ReadAsync(checkout.Root, new string('c', 40), null,
            checkout.Root, TestContext.Current.CancellationToken);

        var file = Assert.Single(result.Files);
        Assert.Equal(oddName, file.Path);
        Assert.Equal(Hash("tracked source"), file.Sha256);
    }

    private static ProcessRequest CheckoutGit(string root, string argument)
    {
        var logRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus git logs " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(logRoot);
        return new ProcessRequest
        {
            ExecutablePath = "git",
            Arguments = ["-C", root, argument],
            WorkingDirectory = root,
            StandardOutputLogPath = Path.Combine(logRoot, Guid.NewGuid().ToString("N") + ".out"),
            StandardErrorLogPath = Path.Combine(logRoot, Guid.NewGuid().ToString("N") + ".err")
        };
    }

    private static string Hash(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Fingerprint(params (string Path, string Hash)[] files)
    {
        var lines = files.Select(static file => $"{file.Path}\t{file.Hash}").Order(StringComparer.Ordinal);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n")));
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        public TemporaryCheckout()
        {
            Root = Path.Combine(Path.GetTempPath(), "LeanCorpus backup matrix " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
