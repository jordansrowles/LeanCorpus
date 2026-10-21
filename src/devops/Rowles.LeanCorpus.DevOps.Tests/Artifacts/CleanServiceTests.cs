using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Artifacts;

public sealed class CleanServiceTests
{
    [Fact]
    public async Task DefaultTargetRemovesLegacyDefaultSetsAndPreservesPackageAndBenchmarkData()
    {
        using var checkout = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var store = new ArtifactRunStore(paths);
        foreach (var relative in new[] { "bin", "obj", "publish", "build/output", "test/output", "coverage/output", "diagnostics/output", "docs/output", "temp/output" })
        {
            CreateFile(paths.ArtifactRoot, relative);
        }

        foreach (var relative in new[] { "package/release/package.nupkg", "benchmark/cache/data/corpus.bin", "benchmark/runs/previous/report.json", "dataforge/reference/corpus.bin" })
        {
            CreateFile(paths.ArtifactRoot, relative);
        }

        var service = new CleanService(paths, store);
        var result = await service.CleanAsync(new CleanRequest { Checkout = checkout.Identity, Target = "default" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.False(File.Exists(Path.Combine(paths.ArtifactRoot, "bin", "output")));
        Assert.False(File.Exists(Path.Combine(paths.ArtifactRoot, "build", "output")));
        Assert.True(File.Exists(Path.Combine(paths.ArtifactRoot, "package", "release", "package.nupkg")));
        Assert.True(File.Exists(Path.Combine(paths.ArtifactRoot, "benchmark", "cache", "data", "corpus.bin")));
        Assert.True(File.Exists(Path.Combine(paths.ArtifactRoot, "benchmark", "runs", "previous", "report.json")));
        Assert.True(File.Exists(Path.Combine(paths.ArtifactRoot, "dataforge", "reference", "corpus.bin")));
    }

    [Fact]
    public async Task BuildTargetPreservesActiveRunAndRemovesOtherOwnedBuildFiles()
    {
        using var checkout = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var store = new ArtifactRunStore(paths);
        var active = await store.StartAsync("build", cancellationToken: TestContext.Current.CancellationToken);
        CreateFile(paths.ArtifactRoot, "build/old/output.log");
        var service = new CleanService(paths, store);

        var result = await service.CleanAsync(new CleanRequest { Checkout = checkout.Identity, Target = "build" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.True(File.Exists(Path.Combine(paths.BuildRunRoot, active.RunId.ToString("D"), "run.json")));
        Assert.False(File.Exists(Path.Combine(paths.ArtifactRoot, "build", "old", "output.log")));
        var cleanManifest = await store.ReadAsync(result.RunId!.Value, TestContext.Current.CancellationToken);
        Assert.NotNull(cleanManifest);
        Assert.Equal(RunState.Succeeded, cleanManifest.State);
    }

    [Fact]
    public async Task AllTargetPreservesBootstrapStateAndRootApphost()
    {
        using var checkout = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var store = new ArtifactRunStore(paths);
        CreateFile(paths.ArtifactRoot, "temp/output");
        CreateFile(paths.ArtifactRoot, "devops/staging/current/payload");
        Directory.CreateDirectory(Path.GetDirectoryName(paths.RootApphostPath)!);
        await File.WriteAllTextAsync(paths.RootApphostPath, "managed apphost", TestContext.Current.CancellationToken);

        var result = await new CleanService(paths, store).CleanAsync(
            new CleanRequest { Checkout = checkout.Identity, Target = "all" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.False(File.Exists(Path.Combine(paths.ArtifactRoot, "temp", "output")));
        Assert.True(File.Exists(Path.Combine(paths.ArtifactRoot, "devops", "staging", "current", "payload")));
        Assert.True(File.Exists(paths.RootApphostPath));
    }

    [Fact]
    public async Task AllTargetPreservesActiveRunsInEverySupportedCategory()
    {
        using var checkout = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var manifests = new List<string>();
        foreach (var category in ArtifactPaths.RunCategories)
        {
            var run = await new ArtifactRunStore(paths, runCategory: category).StartAsync(category,
                cancellationToken: TestContext.Current.CancellationToken);
            manifests.Add(Path.Combine(paths.GetRunDirectory(category, run.RunId), "run.json"));
        }

        var result = await new CleanService(paths, new ArtifactRunStore(paths)).CleanAsync(
            new CleanRequest { Checkout = checkout.Identity, Target = "all" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.All(manifests, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public async Task RejectsUnknownTargetBeforeCreatingRunOrDeletingFiles()
    {
        using var checkout = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var sentinel = CreateFile(paths.ArtifactRoot, "docs/keep.txt");
        var store = new ArtifactRunStore(paths);

        var result = await new CleanService(paths, store).CleanAsync(
            new CleanRequest { Checkout = checkout.Identity, Target = "../docs" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.True(File.Exists(sentinel));
        Assert.False(Directory.Exists(paths.BuildRunRoot));
    }

    [Fact]
    public async Task RejectsTargetLinkToOutsideCheckoutWithoutFollowingIt()
    {
        using var checkout = TemporaryCheckout.Create();
        using var external = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var externalSentinel = CreateFile(external.Root, "outside/keep.txt");
        Directory.CreateDirectory(paths.ArtifactRoot);
        Directory.CreateSymbolicLink(Path.Combine(paths.ArtifactRoot, "docs"), Path.Combine(external.Root, "outside"));

        var result = await new CleanService(paths, new ArtifactRunStore(paths)).CleanAsync(
            new CleanRequest { Checkout = checkout.Identity, Target = "docs" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.ValidationFailure, result.CommandResult.Outcome);
        Assert.True(File.Exists(externalSentinel));
    }

    [Fact]
    public async Task DeletionFailureIsReportedAndRunIsNotMarkedSuccessful()
    {
        using var checkout = TemporaryCheckout.Create();
        var paths = new ArtifactPaths(checkout.Root);
        var failing = CreateFile(paths.ArtifactRoot, "docs/fail.txt");
        var store = new ArtifactRunStore(paths);
        var service = new CleanService(paths, store, new FailingDeleter(failing));

        var result = await service.CleanAsync(new CleanRequest { Checkout = checkout.Identity, Target = "docs" }, TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.InfrastructureFailure, result.CommandResult.Outcome);
        Assert.Contains("injected deletion failure", result.CommandResult.Details, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(failing));
        Assert.Equal(RunState.Failed, result.Manifest!.State);
    }

    private static string CreateFile(string root, string relative)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        return path;
    }

    private sealed class FailingDeleter(string failingPath) : IArtifactPathDeleter
    {
        public void DeleteFile(string path)
        {
            if (Path.GetFullPath(path) == Path.GetFullPath(failingPath))
            {
                throw new IOException("Injected deletion failure.");
            }

            File.Delete(path);
        }

        public void DeleteDirectory(string path) => Directory.Delete(path);
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
            var root = Path.Combine(Path.GetTempPath(), "DevOps clean checkout " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            File.WriteAllText(Path.Combine(root, "global.json"), "{}");
            File.WriteAllText(Path.Combine(root, "Rowles.LeanCorpus.slnx"), "<Solution />");
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
