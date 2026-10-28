using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using OrchardCore.Environment.Shell;
using OrchardCore.Environment.Shell.Scope;
using OrchardCore.Queries;
using Rowles.LeanCorpus.OrchardCore.Search.Storage;

namespace Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests;

[Trait("Area", "Functional")]
[Collection(OrchardBrowserTestCollection.Name)]
public sealed class ProviderShellLifecycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task HostShutdownReleasesWriterWhileReleasedShellScopeIsStillRetained(int repetition)
    {
        string root = Path.Combine(Path.GetTempPath(), "leancorpus-orchard-shell-lifecycle", $"{repetition}-{Guid.NewGuid():N}");
        (WebApplication app, Uri address) = await OrchardSearchTestHost.StartIsolatedAsync(root);
        using var http = new HttpClient { BaseAddress = address };
        ShellScope? oldScope = null;
        var connectionOwners = new HashSet<IDisposable>(ReferenceEqualityComparer.Instance);
        bool stopped = false;
        bool completed = false;
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            IPage page = await browser.NewPageAsync();
            await SearchLifecycleBrowserTests.InstallTestSiteAsync(page, address);

            using HttpResponseMessage created = await http.PostAsync("/__test/vector-probe/LeanCorpus/Search-LeanCorpus", null, TestContext.Current.CancellationToken);
            created.EnsureSuccessStatusCode();
            var probe = await created.Content.ReadFromJsonAsync<OrchardSearchTestHost.VectorProbeSnapshot>(TestContext.Current.CancellationToken);
            Assert.NotNull(probe);
            using HttpResponseMessage indexed = await http.PostAsync(
                $"/__test/vector-probe/LeanCorpus/Search-LeanCorpus/{Uri.EscapeDataString(probe.ContentItemId)}", null, TestContext.Current.CancellationToken);
            indexed.EnsureSuccessStatusCode();

            IShellHost shells = app.Services.GetRequiredService<IShellHost>();
            oldScope = await shells.GetScopeAsync("Default");
            var oldShell = oldScope.ShellContext;
            if (oldScope.ServiceProvider.GetRequiredService<YesSql.IStore>().Configuration.ConnectionFactory is IDisposable oldConnections)
                connectionOwners.Add(oldConnections);
            var oldHandles = oldScope.ServiceProvider.GetRequiredService<LeanCorpusIndexHandleCache>();
            var oldPaths = oldScope.ServiceProvider.GetRequiredService<LeanCorpusIndexPathResolver>().Resolve("Default/Search-LeanCorpus");
            // Resolve and exercise the old provider before replacement, so its
            // shell singleton definitely owns a reference to the physical index.
            IQuerySource source = oldScope.ServiceProvider.GetServices<IQuerySource>().Single(source => source.Name == "LeanCorpus");
            IQueryResults oldResult = await source.ExecuteQueryAsync(new global::OrchardCore.Queries.Query
            {
                Source = "LeanCorpus",
                Schema = """{"index":"Search-LeanCorpus","query":{"type":"vectorKnn","field":"VectorProbe","value":[1,0,0],"topK":1},"take":1}""",
            }, new Dictionary<string, object>());
            Assert.Single(oldResult.Items);

            await shells.ReloadShellContextAsync(oldShell.Settings);
            using HttpResponseMessage queried = await http.PostAsync(
                $"/__test/vector-probe/LeanCorpus/Search-LeanCorpus/{Uri.EscapeDataString(probe.ContentItemId)}", null, TestContext.Current.CancellationToken);
            queried.EnsureSuccessStatusCode();
            var result = await queried.Content.ReadFromJsonAsync<OrchardSearchTestHost.VectorProbeSnapshot>(TestContext.Current.CancellationToken);
            Assert.NotNull(result);
            Assert.Equal(probe.ContentItemId, Assert.Single(result.ResultIds));
            Assert.True(shells.TryGetShellContext("Default", out var replacement));
            Assert.NotSame(oldShell, replacement);
            Assert.True(oldShell.Released);
            Assert.False(oldShell.IsDisposed);
            Assert.True(oldShell.ActiveScopes > 0);
            var oldOwnership = oldHandles.GetDiagnostics();
            await using (ShellScope replacementScope = await shells.GetScopeAsync("Default"))
            {
                if (replacementScope.ServiceProvider.GetRequiredService<YesSql.IStore>().Configuration.ConnectionFactory is IDisposable replacementConnections)
                    connectionOwners.Add(replacementConnections);
                var replacementOwnership = replacementScope.ServiceProvider.GetRequiredService<LeanCorpusIndexHandleCache>().GetDiagnostics();
                Assert.Equal(oldOwnership.SharedStateId, replacementOwnership.SharedStateId);
                Assert.Equal(Assert.Single(oldOwnership.RuntimeIds), Assert.Single(replacementOwnership.RuntimeIds));
                Assert.Contains(oldOwnership.CacheId, replacementOwnership.OwnerCacheIds);
                Console.WriteLine($"Retained shell {oldShell.GetHashCode()}, replacement {replacement.GetHashCode()}, shared state {oldOwnership.SharedStateId}, runtime {Assert.Single(oldOwnership.RuntimeIds)}, shell owners {replacementOwnership.ShellReferences}.");
            }

            string lockPath = Assert.Single(Directory.EnumerateFiles(root, "write.lock", SearchOption.AllDirectories));
            Assert.Throws<IOException>(() => File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose());

            await OrchardSearchTestHost.StopIsolatedAsync(app);
            stopped = true;
            // No retry: terminal host shutdown must release the physical owner
            // even when a historical shell scope has not yet been disposed.
            using (File.Open(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            await Assert.ThrowsAsync<ObjectDisposedException>(() => oldHandles.WithSearcherAsync(oldPaths, _ => true));
            var closedOwnership = oldHandles.GetDiagnostics();
            Assert.True(closedOwnership.CloseCompleted);
            Assert.Equal(0, closedOwnership.ShellReferences);
            Assert.Empty(closedOwnership.RuntimeIds);
            // Prove physical release while the historical shell is still retained.
            // The surrounding Orchard site also contains SQLite and file watchers.
            string closedIndex = oldPaths.IndexDirectory + "-closed";
            Directory.Move(oldPaths.IndexDirectory, closedIndex);
            Directory.Delete(closedIndex, recursive: true);
            completed = true;
        }
        finally
        {
            if (oldScope is not null)
                await oldScope.DisposeAsync();
            if (!stopped)
                await OrchardSearchTestHost.StopIsolatedAsync(app);
            // Released-shell DI disposal can lag behind its last scope. Reclaim
            // test-only SQLite connections after every physical assertion above.
            foreach (IDisposable connectionOwner in connectionOwners)
                connectionOwner.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (completed)
            {
                await OrchardSearchTestHost.DeleteContentRootAsync(root);
            }
            else
                Console.WriteLine($"Orchard shell lifecycle content root retained at {root}");
        }
    }
}
