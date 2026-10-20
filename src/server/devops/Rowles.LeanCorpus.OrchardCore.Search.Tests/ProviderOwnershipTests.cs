using Microsoft.Extensions.Hosting;
using Rowles.LeanCorpus.OrchardCore.Search.Storage;

namespace Rowles.LeanCorpus.OrchardCore.Search.Tests;

[Trait("Area", "Orchard")]
public sealed class ProviderOwnershipTests
{
    [Fact]
    public async Task HostStopInvalidatesHistoricalOwnersAndClosesTheirSharedRuntime()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        var paths = fixture.Paths.Resolve(fixture.Profile.IndexFullName);
        using var lifetime = new ControlledLifetime();
        await using var replacement = new LeanCorpusIndexHandleCache(fixture.Paths, lifetime);
        await replacement.WithSearcherAsync(paths, _ => true);

        var old = fixture.Handles.GetDiagnostics();
        var current = replacement.GetDiagnostics();
        Assert.NotEqual(old.CacheId, current.CacheId);
        Assert.Equal(old.SharedStateId, current.SharedStateId);
        Assert.Equal(2, current.ShellReferences);
        Assert.Contains(old.CacheId, current.OwnerCacheIds);
        Assert.Contains(current.CacheId, current.OwnerCacheIds);
        Assert.Equal(Assert.Single(old.RuntimeIds), Assert.Single(current.RuntimeIds));

        lifetime.StopApplication();
        lifetime.SignalStopped();
        var closed = fixture.Handles.GetDiagnostics();
        Assert.True(closed.AdmissionClosed);
        Assert.True(closed.CloseCompleted);
        Assert.Equal(0, closed.ShellReferences);
        Assert.Empty(closed.RuntimeIds);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Handles.WithSearcherAsync(paths, _ => true));
        using (File.Open(Path.Combine(paths.IndexDirectory, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
    }

    [Fact]
    public async Task HostStopDrainsAdmittedWorkAndRejectsAlreadyQueuedWork()
    {
        await using var fixture = new ProviderFixture();
        Assert.True(await fixture.Indexes.CreateAsync(fixture.Profile));
        var paths = fixture.Paths.Resolve(fixture.Profile.IndexFullName);
        using var lifetime = new ControlledLifetime();
        await using var replacement = new LeanCorpusIndexHandleCache(fixture.Paths, lifetime);
        await replacement.WithSearcherAsync(paths, _ => true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> admitted = fixture.Handles.WithExclusiveAsync(paths, async _ =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            return true;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<bool> queued = replacement.WithExclusiveAsync(paths, _ => true);
            lifetime.StopApplication();
            var quiescing = replacement.GetDiagnostics();
            Assert.True(quiescing.AdmissionClosed);
            Assert.False(quiescing.CloseCompleted);
            Assert.Single(quiescing.RuntimeIds);
            Assert.Throws<IOException>(() => File.Open(Path.Combine(paths.IndexDirectory, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose());
            release.TrySetResult();
            await admitted;
            await Assert.ThrowsAsync<ObjectDisposedException>(() => queued);
            lifetime.SignalStopped();
            Assert.True(replacement.GetDiagnostics().CloseCompleted);
        }
        finally
        {
            release.TrySetResult();
            await admitted;
            lifetime.SignalStopped();
        }
    }

    [Fact]
    public async Task ClosingOneTenantLeavesTheOtherTenantsWriterAndSearcherAvailable()
    {
        string root = Path.Combine(Path.GetTempPath(), "leancorpus-orchard-tenants", Guid.NewGuid().ToString("N"));
        var firstPaths = new LeanCorpusIndexPathResolver(root, "First");
        var secondPaths = new LeanCorpusIndexPathResolver(root, "Second");
        await using var first = new LeanCorpusIndexHandleCache(firstPaths);
        await using var second = new LeanCorpusIndexHandleCache(secondPaths);
        var firstIndex = firstPaths.Resolve("Search");
        var secondIndex = secondPaths.Resolve("Search");
        try
        {
            await first.WithExclusiveAsync(firstIndex, entry => entry.Open(createIfMissing: true).RuntimeId);
            await second.WithExclusiveAsync(secondIndex, entry => entry.Open(createIfMissing: true).RuntimeId);
            Assert.NotEqual(first.GetDiagnostics().SharedStateId, second.GetDiagnostics().SharedStateId);
            await first.DisposeAsync();
            using (File.Open(Path.Combine(firstIndex.IndexDirectory, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            Assert.True(await second.WithSearcherAsync(secondIndex, _ => true));
            Assert.Throws<IOException>(() => File.Open(Path.Combine(secondIndex.IndexDirectory, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose());
            await second.DisposeAsync();
            Directory.Move(root, root + "-closed");
            Directory.Delete(root + "-closed", recursive: true);
        }
        finally
        {
            await first.DisposeAsync();
            await second.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RepeatedShellHandoffsCloseEachPhysicalRuntimeExactlyOnceAtFinalOwnerRelease()
    {
        string root = Path.Combine(Path.GetTempPath(), "leancorpus-orchard-handoffs", Guid.NewGuid().ToString("N"));
        var paths = new LeanCorpusIndexPathResolver(root, "Default");
        var index = paths.Resolve("Search");
        var closes = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var observed = new System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource>();
        using var listener = new System.Diagnostics.ActivityListener
        {
            ShouldListenTo = source => source.Name == "Rowles.LeanCorpus",
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == "leancorpus.index.runtime.close" && activity.GetTagItem("runtime.id") is { } id)
                {
                    closes.AddOrUpdate(id.ToString()!, 1, (_, count) => count + 1);
                    observed.GetOrAdd(id.ToString()!, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
                }
            }
        };
        System.Diagnostics.ActivitySource.AddActivityListener(listener);
        try
        {
            for (int iteration = 0; iteration < 16; iteration++)
            {
                await using var original = new LeanCorpusIndexHandleCache(paths);
                await using var replacement = new LeanCorpusIndexHandleCache(paths);
                Guid runtime = await original.WithExclusiveAsync(index, entry => entry.Open(createIfMissing: true).RuntimeId);
                await replacement.WithSearcherAsync(index, _ => true);
                Assert.Equal(2, replacement.GetDiagnostics().ShellReferences);
                await original.DisposeAsync();
                Assert.Equal(runtime, Assert.Single(replacement.GetDiagnostics().RuntimeIds));
                Assert.True(await replacement.WithSearcherAsync(index, _ => true));
                await replacement.DisposeAsync();
                await replacement.DisposeAsync();
                Assert.True(replacement.GetDiagnostics().CloseCompleted);
                Assert.Equal(0, replacement.GetDiagnostics().ShellReferences);
                await observed.GetOrAdd(runtime.ToString(), _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task
                    .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(1, closes[runtime.ToString()]);
                using (File.Open(Path.Combine(index.IndexDirectory, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            }
            Directory.Delete(root, recursive: true);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ControlledLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _started = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();
        public CancellationToken ApplicationStarted => _started.Token;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;
        public void StopApplication() => _stopping.Cancel();
        internal void SignalStopped() => _stopped.Cancel();
        public void Dispose()
        {
            _started.Dispose();
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }
}
