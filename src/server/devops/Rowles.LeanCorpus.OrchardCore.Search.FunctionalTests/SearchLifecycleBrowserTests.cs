using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;

namespace Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests;

[Trait("Area", "Functional")]
[Collection(OrchardBrowserTestCollection.Name)]
public sealed class SearchLifecycleBrowserTests
{
    [Fact]
    public async Task OrchardSearchTracksPublishedUpdatedAndDeletedContent()
    {
        string root = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("server-tests"), "leancorpus-orchard-browser", Guid.NewGuid().ToString("N"));
        (WebApplication app, Uri address) = await OrchardSearchTestHost.StartIsolatedAsync(root);
        using var http = new HttpClient { BaseAddress = address };
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        IPage page = await browser.NewPageAsync();
        bool completed = false;
        try
        {
            await InstallTestSiteAsync(page, address);
            await SignInAsync(page, address);
            await page.GotoAsync(new Uri(address, "Admin/Indexing").ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.Contains("LeanCorpus", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("Search-LeanCorpus", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);
            Assert.Contains("Search-Lucene", await page.Locator("body").InnerTextAsync(), StringComparison.Ordinal);

            string[]? fieldIndexHandlers = await http.GetFromJsonAsync<string[]>("/__test/field-index-handlers");
            Assert.NotNull(fieldIndexHandlers);
            Assert.Contains("TextFieldIndexHandler", fieldIndexHandlers);

            const string oldTerm = "browserstone";
            const string newTerm = "browsercopper";
            var article = new TestArticle("Search browser lifecycle", $"A deterministic body containing {oldTerm} and orchardcommon.");
            using HttpResponseMessage created = await http.PostAsJsonAsync("/__test/articles", article);
            created.EnsureSuccessStatusCode();
            var createdResult = await created.Content.ReadFromJsonAsync<TestArticleId>();
            Assert.NotNull(createdResult);
            var companion = new TestArticle("Search browser companion", "A companion article containing orchardcommon.");
            using HttpResponseMessage companionCreated = await http.PostAsJsonAsync("/__test/articles", companion);
            companionCreated.EnsureSuccessStatusCode();
            var companionResult = await companionCreated.Content.ReadFromJsonAsync<TestArticleId>();
            Assert.NotNull(companionResult);

            await RebuildAsync(http, "Lucene", "Search-Lucene");
            await RebuildAsync(http, "LeanCorpus", "Search-LeanCorpus");
            await WaitForIndexingCursorAsync(http, "Lucene", "Search-Lucene");
            await WaitForIndexingCursorAsync(http, "LeanCorpus", "Search-LeanCorpus");
            await WaitForSearchResultsAsync(http, "Lucene", "Search-Lucene", oldTerm, [createdResult.ContentItemId]);
            await WaitForSearchResultsAsync(http, "LeanCorpus", "Search-LeanCorpus", oldTerm, [createdResult.ContentItemId]);
            await WaitForSearchResultsAsync(http, "Lucene", "Search-Lucene", "orchardcommon", [createdResult.ContentItemId, companionResult.ContentItemId]);
            await WaitForSearchResultsAsync(http, "LeanCorpus", "Search-LeanCorpus", "orchardcommon", [createdResult.ContentItemId, companionResult.ContentItemId]);
            await AssertSearchPageContainsAsync(page, address, "Search-Lucene", oldTerm, "Search browser lifecycle", expected: true);
            await AssertSearchPageContainsAsync(page, address, "Search-LeanCorpus", oldTerm, "Search browser lifecycle", expected: true);
            await AssertEquivalentSearchAsync(http, oldTerm, [createdResult.ContentItemId]);
            await AssertPagedResultsAsync(http, "orchardcommon", [createdResult.ContentItemId, companionResult.ContentItemId]);

            using HttpResponseMessage reset = await http.PostAsync("/__test/indexes/LeanCorpus/Search-LeanCorpus/reset", content: null);
            Assert.True(reset.IsSuccessStatusCode, $"Orchard LeanCorpus reset returned {(int)reset.StatusCode}.");
            await ProcessIndexingQueueAsync(http);
            await WaitForSearchResultsAsync(http, "LeanCorpus", "Search-LeanCorpus", oldTerm, [createdResult.ContentItemId]);
            await AssertEquivalentSearchAsync(http, oldTerm, [createdResult.ContentItemId]);

            article = article with { Body = $"The replacement body contains {newTerm}." };
            using HttpResponseMessage updated = await http.PutAsJsonAsync($"/__test/articles/{createdResult.ContentItemId}", article);
            updated.EnsureSuccessStatusCode();
            await ProcessIndexingQueueAsync(http);
            await WaitForSearchResultsAsync(http, "Lucene", "Search-Lucene", newTerm, [createdResult.ContentItemId]);
            await WaitForSearchResultsAsync(http, "LeanCorpus", "Search-LeanCorpus", newTerm, [createdResult.ContentItemId]);
            await WaitForSearchResultsAsync(http, "Lucene", "Search-Lucene", oldTerm, []);
            await WaitForSearchResultsAsync(http, "LeanCorpus", "Search-LeanCorpus", oldTerm, []);
            await AssertSearchPageContainsAsync(page, address, "Search-Lucene", newTerm, "Search browser lifecycle", expected: true);
            await AssertSearchPageContainsAsync(page, address, "Search-LeanCorpus", newTerm, "Search browser lifecycle", expected: true);
            await AssertEquivalentSearchAsync(http, oldTerm, []);
            await AssertEquivalentSearchAsync(http, newTerm, [createdResult.ContentItemId]);

            using HttpResponseMessage unpublished = await http.PostAsync($"/__test/articles/{createdResult.ContentItemId}/unpublish", content: null);
            unpublished.EnsureSuccessStatusCode();
            await ProcessIndexingQueueAsync(http);
            await WaitForSearchResultsAsync(http, "Lucene", "Search-Lucene", newTerm, []);
            await WaitForSearchResultsAsync(http, "LeanCorpus", "Search-LeanCorpus", newTerm, []);
            await AssertSearchPageContainsAsync(page, address, "Search-Lucene", newTerm, "Search browser lifecycle", expected: false);
            await AssertSearchPageContainsAsync(page, address, "Search-LeanCorpus", newTerm, "Search browser lifecycle", expected: false);
            await AssertEquivalentSearchAsync(http, newTerm, []);

            using HttpResponseMessage deleted = await http.DeleteAsync($"/__test/articles/{companionResult.ContentItemId}");
            deleted.EnsureSuccessStatusCode();
            await ProcessIndexingQueueAsync(http);
            await WaitForSearchResultsAsync(http, "Lucene", "Search-Lucene", "orchardcommon", []);
            await WaitForSearchResultsAsync(http, "LeanCorpus", "Search-LeanCorpus", "orchardcommon", []);
            await AssertEquivalentSearchAsync(http, "orchardcommon", []);
            completed = true;
        }
        finally
        {
            await OrchardSearchTestHost.StopIsolatedAsync(app);
            if (completed && Directory.Exists(root))
                await OrchardSearchTestHost.DeleteContentRootAsync(root);
            else if (Directory.Exists(root))
                Console.WriteLine($"Orchard diagnostic content root retained at {root}");
        }
    }

    internal static async Task InstallTestSiteAsync(IPage page, Uri address)
    {
        await page.GotoAsync(new Uri(address, "/").ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var setupButton = page.Locator("#SubmitButton");
        if (await setupButton.CountAsync() == 0)
            return;

        await page.Locator("#SiteName").FillAsync("LeanCorpus Orchard Search Test Site");
        await page.Locator("#recipeButton").ClickAsync();
        await page.Locator("#recipes a[data-recipe-name='LeanCorpusSearchTest']").ClickAsync();
        await page.Locator("#SiteTimeZone").SelectOptionAsync("Europe/London");
        await page.Locator("#UserName").FillAsync("admin");
        await page.Locator("#Email").FillAsync("admin@example.test");
        await page.Locator("#Password").FillAsync("OrchardTestPassword!39");
        await page.Locator("#PasswordConfirmation").FillAsync("OrchardTestPassword!39");
        await setupButton.ClickAsync(new LocatorClickOptions { NoWaitAfter = true });
        await setupButton.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Detached,
            Timeout = 120_000,
        });
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
            new PageWaitForLoadStateOptions { Timeout = 120_000 });
        Assert.Equal(0, await page.Locator("#SubmitButton").CountAsync());
    }

    private static async Task SignInAsync(IPage page, Uri address)
    {
        await page.GotoAsync(new Uri(address, "Login").ToString(), new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("input[name='LoginForm.UserName']").FillAsync("admin");
        await page.Locator("input[name='LoginForm.Password']").FillAsync("OrchardTestPassword!39");
        await page.Locator("button[type='submit']").First.ClickAsync();
    }

    private static async Task AssertSearchPageContainsAsync(IPage page, Uri address, string indexName, string term, string title, bool expected)
    {
        string url = new Uri(address, $"search/{Uri.EscapeDataString(indexName)}?terms={Uri.EscapeDataString(term)}").ToString();
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        bool found = (await page.Locator("body").InnerTextAsync()).Contains(title, StringComparison.Ordinal);
        Assert.Equal(expected, found);
    }

    private static async Task RebuildAsync(HttpClient http, string provider, string profile)
    {
        using HttpResponseMessage rebuild = await http.PostAsync($"/__test/indexes/{provider}/{profile}/rebuild", content: null);
        Assert.True(rebuild.IsSuccessStatusCode, $"Orchard {provider} rebuild returned {(int)rebuild.StatusCode}.");
    }

    private static async Task ProcessIndexingQueueAsync(HttpClient http)
    {
        using HttpResponseMessage processed = await http.PostAsync("/__test/indexing/process", content: null);
        Assert.True(processed.IsSuccessStatusCode, $"Orchard content indexing returned {(int)processed.StatusCode}: {await processed.Content.ReadAsStringAsync()}");
    }

    private static async Task WaitForSearchResultsAsync(HttpClient http, string provider, string profile, string term, string[] expectedIds)
    {
        string[] sortedExpected = expectedIds.Order(StringComparer.Ordinal).ToArray();
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            OrchardSearchTestHost.SearchSnapshot snapshot = await GetSnapshotAsync(http, provider, profile, term, 0, Math.Max(10, expectedIds.Length));
            string[] sortedActual = snapshot.ContentItemIds.Order(StringComparer.Ordinal).ToArray();
            if (snapshot.Success && sortedActual.SequenceEqual(sortedExpected, StringComparer.Ordinal))
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        OrchardSearchTestHost.SearchSnapshot final = await GetSnapshotAsync(http, provider, profile, term, 0, Math.Max(10, expectedIds.Length));
        Assert.True(final.Success, $"{provider} search failed for '{term}'.");
        Assert.Equal(sortedExpected, final.ContentItemIds.Order(StringComparer.Ordinal));
    }

    internal static async Task WaitForIndexingCursorAsync(
        HttpClient http,
        string provider,
        string profile,
        CancellationToken cancellationToken = default)
    {
        var timeout = Stopwatch.StartNew();
        while (timeout.Elapsed < TimeSpan.FromSeconds(30))
        {
            OrchardSearchTestHost.IndexingCursorSnapshot cursor = await http.GetFromJsonAsync<OrchardSearchTestHost.IndexingCursorSnapshot>(
                $"/__test/indexes/{provider}/{profile}/cursor",
                cancellationToken)
                ?? throw new InvalidOperationException("Orchard returned no indexing cursor snapshot.");
            Assert.True(cursor.HasContentTasks, $"Orchard created no content indexing tasks for {provider}.");
            if (!cursor.HasPendingContentTasks)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }

        OrchardSearchTestHost.IndexingCursorSnapshot final = await http.GetFromJsonAsync<OrchardSearchTestHost.IndexingCursorSnapshot>(
            $"/__test/indexes/{provider}/{profile}/cursor",
            cancellationToken)
            ?? throw new InvalidOperationException("Orchard returned no indexing cursor snapshot.");
        Assert.False(final.HasPendingContentTasks, $"{provider} stopped at task {final.LastTaskId} while content indexing tasks remained pending.");
    }

    private static async Task AssertEquivalentSearchAsync(HttpClient http, string term, string[] expectedIds)
    {
        var lucene = await GetSnapshotAsync(http, "Lucene", "Search-Lucene", term, 0, 10);
        var leanCorpus = await GetSnapshotAsync(http, "LeanCorpus", "Search-LeanCorpus", term, 0, 10);
        Assert.True(lucene.Success);
        Assert.True(leanCorpus.Success);
        Assert.Equal(expectedIds.Order(StringComparer.Ordinal), lucene.ContentItemIds.Order(StringComparer.Ordinal));
        Assert.Equal(expectedIds.Order(StringComparer.Ordinal), leanCorpus.ContentItemIds.Order(StringComparer.Ordinal));
    }

    private static async Task AssertPagedResultsAsync(HttpClient http, string term, string[] expectedIds)
    {
        var firstPage = await GetSnapshotAsync(http, "LeanCorpus", "Search-LeanCorpus", term, 0, 1);
        var secondPage = await GetSnapshotAsync(http, "LeanCorpus", "Search-LeanCorpus", term, 1, 1);
        Assert.True(firstPage.Success);
        Assert.True(secondPage.Success);
        Assert.Single(firstPage.ContentItemIds);
        Assert.Single(secondPage.ContentItemIds);
        Assert.NotEqual(firstPage.ContentItemIds[0], secondPage.ContentItemIds[0]);
        Assert.Equal(expectedIds.Order(StringComparer.Ordinal), firstPage.ContentItemIds
            .Concat(secondPage.ContentItemIds).Order(StringComparer.Ordinal));
    }

    private static async Task<OrchardSearchTestHost.SearchSnapshot> GetSnapshotAsync(
        HttpClient http,
        string provider,
        string profile,
        string term,
        int start,
        int size)
        => await http.GetFromJsonAsync<OrchardSearchTestHost.SearchSnapshot>(
            $"/__test/search/{provider}/{profile}?terms={Uri.EscapeDataString(term)}&start={start}&size={size}")
            ?? throw new InvalidOperationException("Orchard returned no search snapshot.");

    private sealed record TestArticle(string Title, string Body);
    private sealed record TestArticleId(string ContentItemId);
}
