using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Testing;

public sealed class PlaywrightRuntimePreflightTests
{
    [Fact]
    public async Task ValidatesPinnedResolvedPackageAndLaunchesHeadlessChromium()
    {
        using var fixture = new PreflightFixture();
        var factory = fixture.CreateSessionFactory();
        var preflight = new PlaywrightRuntimePreflight(factory);

        var result = await preflight.ValidateAsync(fixture.Identity, [fixture.BrowserTarget], TestContext.Current.CancellationToken);

        var runtime = Assert.Single(result);
        Assert.Equal("1.63.0", runtime.PackageVersion);
        Assert.Equal("Chromium test", runtime.ChromiumVersion);
        Assert.Equal(factory.Session.ChromiumExecutablePath, runtime.ChromiumExecutablePath);
        Assert.Equal(1, factory.CallCount);
        Assert.Equal(1, factory.Session.LaunchCount);
        Assert.Equal(1, factory.Session.DisposeCount);
        Assert.Equal(Path.Combine(fixture.OutputDirectory, "Microsoft.Playwright.dll"), factory.AssemblyPath);
    }

    [Fact]
    public async Task RejectsAResolvedPackageIdentityThatDoesNotMatchTheCentralPin()
    {
        using var fixture = new PreflightFixture();
        fixture.WriteDependencyManifest("Microsoft.Playwright/1.62.0");
        var factory = fixture.CreateSessionFactory();
        var preflight = new PlaywrightRuntimePreflight(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preflight.ValidateAsync(fixture.Identity, [fixture.BrowserTarget], TestContext.Current.CancellationToken));

        Assert.Contains("Microsoft.Playwright/1.63.0", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public async Task RequiresExactlyOneDependencyManifestWithThePinnedPackageIdentity()
    {
        using var fixture = new PreflightFixture();
        fixture.WriteDependencyManifest("Microsoft.Playwright/1.63.0", "second.deps.json");
        var factory = fixture.CreateSessionFactory();
        var preflight = new PlaywrightRuntimePreflight(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preflight.ValidateAsync(fixture.Identity, [fixture.BrowserTarget], TestContext.Current.CancellationToken));

        Assert.Contains("found 2", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, factory.CallCount);
    }

    [Fact]
    public async Task DoesNotLaunchWhenThePinnedChromiumExecutableIsMissing()
    {
        using var fixture = new PreflightFixture();
        var factory = fixture.CreateSessionFactory(chromiumExists: false);
        var preflight = new PlaywrightRuntimePreflight(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preflight.ValidateAsync(fixture.Identity, [fixture.BrowserTarget], TestContext.Current.CancellationToken));

        Assert.Contains("Chromium executable", exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, factory.Session.LaunchCount);
        Assert.Equal(1, factory.Session.DisposeCount);
    }

    [Fact]
    public async Task ReportsBrowserLaunchFailuresAsManagedPreflightFailures()
    {
        using var fixture = new PreflightFixture();
        var factory = fixture.CreateSessionFactory(launchFailure: new InvalidOperationException("Chromium could not start."));
        var preflight = new PlaywrightRuntimePreflight(factory);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            preflight.ValidateAsync(fixture.Identity, [fixture.BrowserTarget], TestContext.Current.CancellationToken));

        Assert.Contains("Chromium could not start", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, factory.Session.LaunchCount);
        Assert.Equal(1, factory.Session.DisposeCount);
    }

    private sealed class PreflightFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "LeanCorpus Playwright preflight " + Guid.NewGuid().ToString("N"));
        public string OutputDirectory { get; }
        public CheckoutIdentity Identity { get; }
        public PreparedTestTarget BrowserTarget { get; }

        public PreflightFixture()
        {
            OutputDirectory = Path.Combine(Root, "artifacts", "bin", "docs", "release_net10.0");
            Directory.CreateDirectory(OutputDirectory);
            Identity = new CheckoutIdentity(Root, null, null);
            File.WriteAllText(Path.Combine(Root, "Directory.Packages.props"),
                "<Project><ItemGroup><PackageVersion Include=\"Microsoft.Playwright\" Version=\"1.63.0\" /></ItemGroup></Project>");
            File.WriteAllText(Path.Combine(OutputDirectory, "Microsoft.Playwright.dll"), "test assembly");
            WriteDependencyManifest("Microsoft.Playwright/1.63.0");
            var project = Path.Combine("src", "tests", "docs.csproj");
            BrowserTarget = new PreparedTestTarget(new TestTarget("docs-site/net10.0", "Docs site", "docs-site", "Mtp",
                project, "net10.0", "Release", "", "", [], false, ["Trx"], []) { RequiresBrowser = true },
                Path.Combine(Root, project), Path.Combine(OutputDirectory, "docs"));
        }

        public RecordingSessionFactory CreateSessionFactory(bool chromiumExists = true, Exception? launchFailure = null)
        {
            var chromium = Path.Combine(OutputDirectory, "chromium");
            if (chromiumExists)
            {
                File.WriteAllText(chromium, "test executable");
            }
            return new RecordingSessionFactory(chromium, launchFailure);
        }

        public void WriteDependencyManifest(string identity, string fileName = "docs.deps.json")
        {
            var libraries = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [identity] = new { type = "package" }
            };
            File.WriteAllText(Path.Combine(OutputDirectory, fileName), JsonSerializer.Serialize(new { libraries }));
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class RecordingSessionFactory(string chromiumPath, Exception? launchFailure) : IPlaywrightRuntimeSessionFactory
    {
        public int CallCount { get; private set; }
        public string? AssemblyPath { get; private set; }
        public RecordingSession Session { get; } = new(chromiumPath, launchFailure);

        public Task<IPlaywrightRuntimeSession> CreateAsync(string assemblyPath, CancellationToken cancellationToken = default)
        {
            CallCount++;
            AssemblyPath = assemblyPath;
            return Task.FromResult<IPlaywrightRuntimeSession>(Session);
        }
    }

    private sealed class RecordingSession(string chromiumExecutablePath, Exception? launchFailure) : IPlaywrightRuntimeSession
    {
        public string ChromiumExecutablePath { get; } = chromiumExecutablePath;
        public int LaunchCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task<string> LaunchHeadlessAsync(CancellationToken cancellationToken = default)
        {
            LaunchCount++;
            if (launchFailure is not null)
            {
                return Task.FromException<string>(launchFailure);
            }
            return Task.FromResult("Chromium test");
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
