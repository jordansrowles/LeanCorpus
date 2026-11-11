using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using Rowles.LeanCorpus.OrchardCore.Search.Storage;

namespace Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests;

[Trait("Area", "Functional")]
[Collection(OrchardBrowserTestCollection.Name)]
public sealed class ProviderRecoveryBrowserTests
{
    private const string Provider = "LeanCorpus";
    private const string Profile = "Search-LeanCorpus";

    [Fact]
    public async Task OrchardIndexingPipelineReplaysEveryInjectedProviderFailureSafely()
    {
        string root = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("server-tests"), "leancorpus-orchard-recovery", Guid.NewGuid().ToString("N"));
        WebApplication? app = null;
        HttpClient? http = null;
        bool completed = false;
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            (WebApplication startedApp, Uri address) = await OrchardSearchTestHost.StartIsolatedAsync(root);
            app = startedApp;
            http = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            IPage page = await browser.NewPageAsync();
            await SearchLifecycleBrowserTests.InstallTestSiteAsync(page, address);

            await RebuildIndexAsync(http, cancellationToken);
            await ProcessAllIndexesAsync(http, cancellationToken);
            await AssertRecoveryAsync("R1", LeanCorpusFailurePoint.BeforeDocumentWrite, "recoveryr1", documentPresentAfterFailure: false);
            await AssertRecoveryAsync("R2", LeanCorpusFailurePoint.AfterDocumentWriteBeforeCommit, "recoveryr2", documentPresentAfterFailure: false);
            await AssertRecoveryAsync("R3", LeanCorpusFailurePoint.AfterCommitBeforeCursor, "recoveryr3", documentPresentAfterFailure: true);
            await AssertRecoveryAsync("R4", LeanCorpusFailurePoint.DuringCursorWrite, "recoveryr4", documentPresentAfterFailure: true);
            await AssertRecoveryAsync("R5", LeanCorpusFailurePoint.AfterCursorFlushBeforePublish, "recoveryr5", documentPresentAfterFailure: true);
            await AssertRecoveryAsync("R6", LeanCorpusFailurePoint.AfterCursorPublish, "recoveryr6", documentPresentAfterFailure: true, cursorPublished: true);
            await AssertDeleteRecoveryAsync();
            await AssertSchemaRecoveryAsync(root);
            completed = true;

            async Task AssertRecoveryAsync(
                string name,
                LeanCorpusFailurePoint failurePoint,
                string term,
                bool documentPresentAfterFailure,
                bool cursorPublished = false)
            {
                OrchardSearchTestHost.IndexingCursorSnapshot before = await ReadCursorAsync(http!, cancellationToken);
                string contentItemId = await CreateArticleAsync(http!, term, cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot queued = await ReadCursorAsync(http!, cancellationToken);
                Assert.True(queued.LatestTaskId > before.LastTaskId, $"{name} created no durable Orchard indexing task.");

                await ArmFailureAsync(http!, failurePoint, cancellationToken);
                await PostIndexingProcessAsync(http!, cancellationToken);
                Assert.Equal(failurePoint.ToString(), await ReadLastTriggeredAsync(http!, cancellationToken));
                OrchardSearchTestHost.IndexingCursorSnapshot failed = await ReadCursorAsync(http!, cancellationToken);
                Assert.True(failed.LastTaskId <= failed.LatestTaskId, $"{name} cursor exceeded durable Orchard work.");
                Assert.Equal(cursorPublished ? queued.LatestTaskId : before.LastTaskId, failed.LastTaskId);
                await AssertArticlePresentAsync(http!, term, contentItemId, documentPresentAfterFailure, cancellationToken);

                (app, _, http) = await RestartAsync(app!, http!, root, cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot restarted = await ReadCursorAsync(http, cancellationToken);
                Assert.True(restarted.LastTaskId <= restarted.LatestTaskId, $"{name} cursor exceeded durable Orchard work after restart.");
                Assert.Equal(failed.LastTaskId, restarted.LastTaskId);
                await AssertArticlePresentAsync(http, term, contentItemId, documentPresentAfterFailure, cancellationToken);

                await ProcessIndexAsync(http, cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot replayed = await ReadCursorAsync(http, cancellationToken);
                Assert.True(replayed.LastTaskId <= replayed.LatestTaskId, $"{name} cursor exceeded durable Orchard work after replay.");
                Assert.False(replayed.HasPendingContentTasks, $"{name} left durable Orchard work pending after replay.");
                await AssertArticlePresentAsync(http, term, contentItemId, expected: true, cancellationToken);
                OrchardSearchTestHost.SearchSnapshot final = await SearchAsync(http, term, cancellationToken);
                Assert.Equal(1, final.TotalCount);
                Assert.Equal(contentItemId, Assert.Single(final.ContentItemIds));

                Console.WriteLine($"RECOVERY {name}: point={failurePoint}; cursorBefore={before.LastTaskId}; cursorAfterRestart={restarted.LastTaskId}; latestTaskId={restarted.LatestTaskId}; cursorAfterReplay={replayed.LastTaskId}; documentAfterFailure={documentPresentAfterFailure}; replay=passed; invariant=passed");
            }

            async Task AssertDeleteRecoveryAsync()
            {
                const string term = "recoveryr7";
                string contentItemId = await CreateArticleAsync(http!, term, cancellationToken);
                await ProcessIndexAsync(http!, cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot before = await ReadCursorAsync(http!, cancellationToken);
                Assert.False(before.HasPendingContentTasks);
                await AssertArticlePresentAsync(http!, term, contentItemId, expected: true, cancellationToken);

                await EnqueueTaskAsync(http!, contentItemId, "Delete", cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot queued = await ReadCursorAsync(http!, cancellationToken);
                Assert.True(queued.LatestTaskId > before.LastTaskId, "R7 created no durable Orchard delete task.");

                await ArmFailureAsync(http!, LeanCorpusFailurePoint.AfterDeleteCommitBeforeCursor, cancellationToken);
                OrchardSearchTestHost.DeleteTaskProcessingSnapshot failedProcessing = await ProcessDeleteTaskAsync(http!, cancellationToken);
                Assert.True(failedProcessing.FoundTask);
                Assert.False(failedProcessing.Succeeded);
                Assert.Equal(nameof(LeanCorpusFailurePoint.AfterDeleteCommitBeforeCursor), await ReadLastTriggeredAsync(http!, cancellationToken));
                OrchardSearchTestHost.IndexingCursorSnapshot failed = await ReadCursorAsync(http!, cancellationToken);
                Assert.Equal(before.LastTaskId, failed.LastTaskId);
                Assert.True(failed.LastTaskId <= failed.LatestTaskId, "R7 cursor exceeded durable Orchard work.");
                Assert.Equal(failed.LastTaskId, failedProcessing.LastTaskId);
                await AssertArticlePresentAsync(http!, term, contentItemId, expected: false, cancellationToken);

                (app, _, http) = await RestartAsync(app!, http!, root, cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot restarted = await ReadCursorAsync(http!, cancellationToken);
                Assert.Equal(failed.LastTaskId, restarted.LastTaskId);
                Assert.True(restarted.LastTaskId <= restarted.LatestTaskId, "R7 cursor exceeded durable Orchard work after restart.");
                await AssertArticlePresentAsync(http!, term, contentItemId, expected: false, cancellationToken);
                OrchardSearchTestHost.DeleteTaskProcessingSnapshot replay = await ProcessDeleteTaskAsync(http!, cancellationToken);
                Assert.True(replay.FoundTask);
                Assert.True(replay.Succeeded);
                OrchardSearchTestHost.IndexingCursorSnapshot replayed = await ReadCursorAsync(http!, cancellationToken);
                Assert.True(replayed.LastTaskId > restarted.LastTaskId);
                Assert.False(replayed.HasPendingContentTasks);
                Assert.True(replayed.LastTaskId <= replayed.LatestTaskId, "R7 cursor exceeded durable Orchard work after replay.");
                await AssertArticlePresentAsync(http!, term, contentItemId, expected: false, cancellationToken);
                Console.WriteLine($"RECOVERY R7: point={nameof(LeanCorpusFailurePoint.AfterDeleteCommitBeforeCursor)}; cursorBefore={before.LastTaskId}; cursorAfterRestart={restarted.LastTaskId}; latestTaskId={restarted.LatestTaskId}; cursorAfterReplay={replayed.LastTaskId}; deleteAfterFailure=durable; replay=passed; invariant=passed");
            }

            async Task AssertSchemaRecoveryAsync(string contentRoot)
            {
                OrchardSearchTestHost.IndexingCursorSnapshot before = await ReadCursorAsync(http!, cancellationToken);
                OrchardSearchTestHost.VectorProbeSnapshot created;
                using (HttpResponseMessage response = await http!.PostAsync("/__test/vector-probe/LeanCorpus/Search-LeanCorpus", content: null, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    created = await response.Content.ReadFromJsonAsync<OrchardSearchTestHost.VectorProbeSnapshot>(cancellationToken)
                        ?? throw new InvalidOperationException("Orchard returned no vector probe creation result.");
                }
                OrchardSearchTestHost.IndexingCursorSnapshot queued = await ReadCursorAsync(http!, cancellationToken);
                Assert.True(queued.LatestTaskId > before.LastTaskId, "R8 created no durable Orchard indexing task.");

                await ArmFailureAsync(http!, LeanCorpusFailurePoint.AfterSchemaPublishBeforeCommit, cancellationToken);
                await PostIndexingProcessAsync(http!, cancellationToken);
                Assert.Equal(nameof(LeanCorpusFailurePoint.AfterSchemaPublishBeforeCommit), await ReadLastTriggeredAsync(http!, cancellationToken));
                OrchardSearchTestHost.IndexingCursorSnapshot failed = await ReadCursorAsync(http!, cancellationToken);
                Assert.Equal(before.LastTaskId, failed.LastTaskId);
                Assert.True(failed.LastTaskId <= failed.LatestTaskId, "R8 cursor exceeded durable Orchard work.");
                Assert.Contains("VectorProbe", ReadPublishedSchemaFields(contentRoot));
                await AssertVectorResultsAsync(http!, created.ContentItemId, [], cancellationToken);

                (app, _, http) = await RestartAsync(app!, http!, root, cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot restarted = await ReadCursorAsync(http!, cancellationToken);
                Assert.Equal(failed.LastTaskId, restarted.LastTaskId);
                Assert.True(restarted.LastTaskId <= restarted.LatestTaskId, "R8 cursor exceeded durable Orchard work after restart.");
                await AssertVectorResultsAsync(http!, created.ContentItemId, [], cancellationToken);
                await ProcessIndexAsync(http!, cancellationToken);
                OrchardSearchTestHost.IndexingCursorSnapshot replayed = await ReadCursorAsync(http!, cancellationToken);
                Assert.False(replayed.HasPendingContentTasks);
                await AssertVectorResultsAsync(http!, created.ContentItemId, [created.ContentItemId], cancellationToken);
                Console.WriteLine($"RECOVERY R8: point={nameof(LeanCorpusFailurePoint.AfterSchemaPublishBeforeCommit)}; cursorBefore={before.LastTaskId}; cursorAfterRestart={restarted.LastTaskId}; latestTaskId={restarted.LatestTaskId}; cursorAfterReplay={replayed.LastTaskId}; schemaPublished=true; documentAfterFailure=false; replay=passed; invariant=passed");
            }
        }
        finally
        {
            http?.Dispose();
            if (app is not null)
            {
                await OrchardSearchTestHost.StopIsolatedAsync(app);
            }

            if (completed && Directory.Exists(root))
                await OrchardSearchTestHost.DeleteContentRootAsync(root);
            else if (Directory.Exists(root))
                Console.WriteLine($"Orchard recovery content root retained at {root}");
        }
    }

    private static async Task<(WebApplication App, Uri Address, HttpClient Client)> RestartAsync(
        WebApplication app,
        HttpClient client,
        string root,
        CancellationToken cancellationToken)
    {
        client.Dispose();
        await OrchardSearchTestHost.StopIsolatedAsync(app);
        (WebApplication restarted, Uri address) = await OrchardSearchTestHost.StartIsolatedAsync(root);
        return (restarted, address, new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) });
    }

    private static async Task ProcessAllIndexesAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync("/__test/indexing/process", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task RebuildIndexAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync($"/__test/indexes/{Provider}/{Profile}/rebuild", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task ProcessIndexAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync($"/__test/indexing/process/{Provider}/{Profile}", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task PostIndexingProcessAsync(HttpClient http, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync($"/__test/indexing/process/{Provider}/{Profile}", content: null, cancellationToken);
        // Faults may bubble out of Orchard's indexing service or be logged and swallowed.
        // The one-shot internal injector is the authoritative signal that this request hit the fault.
        _ = response.StatusCode;
    }

    private static async Task ArmFailureAsync(HttpClient http, LeanCorpusFailurePoint point, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync($"/__test/failures/{point}", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<string?> ReadLastTriggeredAsync(HttpClient http, CancellationToken cancellationToken)
        => (await http.GetFromJsonAsync<OrchardSearchTestHost.FailureSnapshot>("/__test/failures", cancellationToken))?.LastTriggered;

    private static async Task<OrchardSearchTestHost.IndexingCursorSnapshot> ReadCursorAsync(HttpClient http, CancellationToken cancellationToken)
        => await http.GetFromJsonAsync<OrchardSearchTestHost.IndexingCursorSnapshot>(
            $"/__test/indexes/{Provider}/{Profile}/cursor", cancellationToken)
            ?? throw new InvalidOperationException("Orchard returned no cursor snapshot.");

    private static async Task<string> CreateArticleAsync(HttpClient http, string term, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(
            "/__test/recovery/articles",
            new OrchardSearchTestHost.TestArticleRequest($"Recovery {term}", $"Durable Orchard body includes {term}."),
            cancellationToken);
        response.EnsureSuccessStatusCode();
        OrchardSearchTestHost.TestArticleResponse result = await response.Content.ReadFromJsonAsync<OrchardSearchTestHost.TestArticleResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Orchard returned no recovery article identity.");
        await EnqueueTaskAsync(http, result.ContentItemId, "Update", cancellationToken);
        return result.ContentItemId;
    }

    private static async Task EnqueueTaskAsync(HttpClient http, string contentItemId, string taskType, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync(
            $"/__test/recovery/tasks/{Uri.EscapeDataString(contentItemId)}/{Uri.EscapeDataString(taskType)}",
            content: null,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<OrchardSearchTestHost.DeleteTaskProcessingSnapshot> ProcessDeleteTaskAsync(
        HttpClient http,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsync(
            $"/__test/recovery/process-delete/{Provider}/{Profile}", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<OrchardSearchTestHost.DeleteTaskProcessingSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("Orchard returned no delete task processing result.");
    }

    private static async Task AssertArticlePresentAsync(
        HttpClient http,
        string term,
        string contentItemId,
        bool expected,
        CancellationToken cancellationToken)
    {
        OrchardSearchTestHost.SearchSnapshot result = await SearchAsync(http, term, cancellationToken);
        Assert.True(result.Success, $"LeanCorpus search failed for recovery term '{term}'.");
        Assert.Equal(expected ? 1 : 0, result.TotalCount);
        if (expected)
            Assert.Equal(contentItemId, Assert.Single(result.ContentItemIds));
        else
            Assert.Empty(result.ContentItemIds);
    }

    private static async Task<OrchardSearchTestHost.SearchSnapshot> SearchAsync(HttpClient http, string term, CancellationToken cancellationToken)
        => await http.GetFromJsonAsync<OrchardSearchTestHost.SearchSnapshot>(
            $"/__test/search/{Provider}/{Profile}?terms={Uri.EscapeDataString(term)}&start=0&size=10", cancellationToken)
            ?? throw new InvalidOperationException("Orchard returned no recovery search result.");

    private static async Task AssertVectorResultsAsync(
        HttpClient http,
        string contentItemId,
        string[] expectedIds,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.GetAsync(
            $"/__test/vector-probe/query/{Provider}/{Profile}/{Uri.EscapeDataString(contentItemId)}", cancellationToken);
        response.EnsureSuccessStatusCode();
        OrchardSearchTestHost.VectorProbeSnapshot result = await response.Content.ReadFromJsonAsync<OrchardSearchTestHost.VectorProbeSnapshot>(cancellationToken)
            ?? throw new InvalidOperationException("Orchard returned no vector query result.");
        Assert.Equal(expectedIds, result.ResultIds);
    }

    private static string[] ReadPublishedSchemaFields(string contentRoot)
    {
        string sitesPath = Path.Combine(contentRoot, "App_Data", "Sites");
        string schemaPath = Directory.EnumerateFiles(sitesPath, "*.json", SearchOption.AllDirectories)
            .Single(path => Path.GetRelativePath(sitesPath, path)
                .Split(Path.DirectorySeparatorChar)
                .TakeLast(3)
                .SequenceEqual(["LeanCorpus", "schemas", Path.GetFileName(path)], StringComparer.Ordinal));
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        return schema.RootElement.GetProperty("fields").EnumerateObject().Select(field => field.Name).ToArray();
    }
}
