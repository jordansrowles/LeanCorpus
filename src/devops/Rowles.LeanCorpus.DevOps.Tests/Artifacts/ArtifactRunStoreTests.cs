using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;

namespace Rowles.LeanCorpus.DevOps.Tests.Artifacts;

public sealed class ArtifactRunStoreTests
{
    [Fact]
    public async Task WritesRunningAndTerminalManifestWithIdentityAndProvenance()
    {
        using var root = TemporaryRoot.Create();
        var paths = new ArtifactPaths(root.Path);
        var store = new ArtifactRunStore(paths);
        var started = await store.StartAsync(
            "build",
            new Dictionary<string, string> { ["configuration"] = "Release" },
            sourceCommit: "abc123",
            sourceDirty: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, started.RunId);
        Assert.Equal(RunState.Running, started.State);
        var manifestPath = Path.Combine(paths.BuildRunRoot, started.RunId.ToString("D"), "run.json");
        Assert.True(File.Exists(manifestPath));
        Assert.Contains("\"state\": \"running\"", await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal("abc123", started.SourceCommit);
        Assert.True(started.SourceDirty);

        var finished = await store.FinishAsync(
            started,
            RunState.Succeeded,
            childExitCode: 0,
            artifacts: ["artifacts/build/output.bin"],
            cancellationToken: TestContext.Current.CancellationToken);
        var read = await store.ReadAsync(started.RunId, TestContext.Current.CancellationToken);

        Assert.Equal(RunState.Succeeded, finished.State);
        Assert.NotNull(finished.CompletedUtc);
        Assert.Equal([0], finished.ChildExitCodes);
        Assert.Equal(["artifacts/build/output.bin"], finished.Artifacts);
        Assert.NotNull(read);
        Assert.Equal(finished.RunId, read.RunId);
        Assert.Equal(finished.State, read.State);
        Assert.Contains("\"state\": \"succeeded\"", await File.ReadAllTextAsync(manifestPath, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(finished.SourceCommit, read.SourceCommit);
        Assert.Equal(finished.ChildExitCodes, read.ChildExitCodes);
        Assert.Equal(finished.Artifacts, read.Artifacts);
    }

    [Fact]
    public async Task MarksOrphanedRunningManifestInterruptedOnNextRead()
    {
        using var root = TemporaryRoot.Create();
        var paths = new ArtifactPaths(root.Path);
        var runId = Guid.NewGuid();
        var runDirectory = paths.CreateBuildRunDirectory(runId);
        var orphaned = new RunManifest
        {
            SchemaVersion = RunManifest.CurrentSchemaVersion,
            RunId = runId,
            Command = "test",
            State = RunState.Running,
            StartedUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
            SourceDirty = false,
            Platform = "test platform",
            Inputs = new Dictionary<string, string>(),
            ChildExitCodes = [],
            Artifacts = [],
            OwnerProcessId = int.MaxValue,
            OwnerProcessStartedUtc = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await File.WriteAllTextAsync(
            Path.Combine(runDirectory, "run.json"),
            JsonSerializer.Serialize(orphaned, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
            }),
            TestContext.Current.CancellationToken);
        var store = new ArtifactRunStore(paths);

        var recovered = await store.ReadAsync(runId, TestContext.Current.CancellationToken);
        var persisted = await store.ReadAsync(runId, TestContext.Current.CancellationToken);

        Assert.NotNull(recovered);
        Assert.Equal(RunState.Interrupted, recovered.State);
        Assert.NotNull(recovered.CompletedUtc);
        Assert.Contains("owner process ended", recovered.Failure, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(persisted);
        Assert.Equal(RunState.Interrupted, persisted.State);
        Assert.Equal(recovered.RunId, persisted.RunId);
    }

    [Fact]
    public async Task FailedStartWriteDoesNotReturnOrPersistSuccessEvidence()
    {
        using var root = TemporaryRoot.Create();
        var paths = new ArtifactPaths(root.Path);
        var store = new ArtifactRunStore(paths, new FailingWriter());

        await Assert.ThrowsAsync<IOException>(() => store.StartAsync("build", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(paths.BuildRunRoot, "run.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RejectsUnsupportedSchemaAndRepeatedFinalisation()
    {
        using var root = TemporaryRoot.Create();
        var store = new ArtifactRunStore(new ArtifactPaths(root.Path));
        var started = await store.StartAsync("clean", cancellationToken: TestContext.Current.CancellationToken);
        var finished = await store.FinishAsync(started, RunState.Failed, failure: "expected", cancellationToken: TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FinishAsync(finished, RunState.Succeeded, cancellationToken: TestContext.Current.CancellationToken));
        var path = Path.Combine(root.Path, "artifacts", "build", "runs", started.RunId.ToString("D"), "run.json");
        var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(path, json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99", StringComparison.Ordinal), TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync(started.RunId, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadsHistoricalManifestAndWritesCanonicalRunWithoutReplacingHistory()
    {
        using var root = TemporaryRoot.Create();
        var paths = new ArtifactPaths(root.Path);
        var store = new ArtifactRunStore(paths);
        var started = await store.StartAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        var directory = paths.GetBuildRunDirectory(started.RunId);
        var canonical = Path.Combine(directory, "run.json");
        var historical = Path.Combine(directory, "manifest.json");
        File.Move(canonical, historical);
        var original = await File.ReadAllTextAsync(historical, TestContext.Current.CancellationToken);

        var recovered = await store.ReadAsync(started.RunId, TestContext.Current.CancellationToken);
        Assert.NotNull(recovered);
        await store.FinishAsync(recovered, RunState.Succeeded, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(File.Exists(canonical));
        Assert.Equal(original, await File.ReadAllTextAsync(historical, TestContext.Current.CancellationToken));
        Assert.Equal(RunState.Succeeded, (await store.ReadAsync(started.RunId, TestContext.Current.CancellationToken))!.State);

        await File.WriteAllTextAsync(canonical, "{invalid", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<JsonException>(() => store.ReadAsync(started.RunId, TestContext.Current.CancellationToken));
    }

    private sealed class FailingWriter : IAtomicFileWriter
    {
        public Task WriteReplaceAsync(string path, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
            => throw new IOException("Injected atomic manifest write failure.");
    }

    private sealed class TemporaryRoot : IDisposable
    {
        private TemporaryRoot(string path) => Path = path;

        public string Path { get; }

        public static TemporaryRoot Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DevOps run store " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryRoot(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
