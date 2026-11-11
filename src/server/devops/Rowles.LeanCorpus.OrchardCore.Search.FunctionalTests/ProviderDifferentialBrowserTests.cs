using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Playwright;
using Rowles.LeanCorpus.OrchardCore.Search.TestModule.Indexing;

namespace Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests;

[Trait("Area", "Functional")]
[Collection(OrchardBrowserTestCollection.Name)]
public sealed class ProviderDifferentialBrowserTests
{
    [Fact]
    public async Task OrchardLuceneAndLeanCorpusReturnTheSameNumericAndDatePredicateIds()
    {
        string root = Path.Combine(RepositoryTemporaryStorage.GetAreaRoot("server-tests"), "leancorpus-orchard-differential", Guid.NewGuid().ToString("N"));
        (WebApplication app, Uri address) = await OrchardSearchTestHost.StartIsolatedAsync(root);
        using var http = new HttpClient { BaseAddress = address };
        bool completed = false;
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            IPage page = await browser.NewPageAsync();
            await SearchLifecycleBrowserTests.InstallTestSiteAsync(page, address);

            string[] ids = new string[4];
            for (int index = 0; index < ids.Length; index++)
            {
                int sequence = (index + 1) * 10;
                using HttpResponseMessage created = await http.PostAsJsonAsync("/__test/articles", new OrchardSearchTestHost.TestArticleRequest(
                    $"Differential record {sequence}",
                    index == 0
                        ? $"The deterministic differential body for record {sequence}: Crème 東京 🚀 co-op punctuation."
                        : $"The deterministic differential body for record {sequence}.",
                    Category: index < 3 ? "sharedcategory" : null!,
                    PublishedUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(index * 10).ToString("O"),
                    Sequence: sequence,
                    Rating: index + 0.5,
                    Featured: index is 0 or 2,
                    Keywords: index < 2 ? $"sharedmember member-{sequence}" : $"othermember member-{sequence}"));
                created.EnsureSuccessStatusCode();
                ids[index] = (await created.Content.ReadFromJsonAsync<OrchardSearchTestHost.TestArticleResponse>())
                    ?.ContentItemId ?? throw new InvalidOperationException("Orchard returned no differential content ID.");
            }

            using (HttpResponseMessage processed = await http.PostAsync("/__test/indexing/process", content: null))
                processed.EnsureSuccessStatusCode();

            await AssertEquivalentAsync(
                "keyword-shaped text term",
                """{"type":"term","field":"SearchTestArticle.Category","value":"sharedcategory"}""",
                """{"term":{"SearchTestArticle.Category":"sharedcategory"}}""",
                [ids[0], ids[1], ids[2]]);
            await AssertEquivalentAsync(
                "integer range",
                """{"type":"range","field":"SearchTestArticle.Sequence","gte":20,"lte":30}""",
                """{"range":{"SearchTestArticle.Sequence":{"gte":20,"lte":30}}}""",
                [ids[1], ids[2]]);
            await AssertEquivalentAsync(
                "number range",
                """{"type":"range","field":"SearchTestArticle.Rating","gte":1.5,"lte":3.5}""",
                """{"range":{"SearchTestArticle.Rating":{"gte":1.5,"lte":3.5}}}""",
                [ids[1], ids[2], ids[3]]);
            // Orchard Lucene's encoded date range excludes the lower endpoint in this query path,
            // so use a wider Lucene interval to compare the two records inside the LeanCorpus range.
            string luceneDateRange = JsonSerializer.Serialize(new
            {
                range = new Dictionary<string, object>
                {
                    ["SearchTestArticle.PublishedUtc"] = new
                    {
                        gte = OrchardSearchTestHost.FormatLuceneDate(new DateTimeOffset(2026, 1, 10, 0, 0, 0, TimeSpan.Zero)),
                        lte = OrchardSearchTestHost.FormatLuceneDate(new DateTimeOffset(2026, 1, 22, 0, 0, 0, TimeSpan.Zero)),
                    },
                },
            });
            await AssertEquivalentAsync(
                "DateTime range",
                """{"type":"range","field":"SearchTestArticle.PublishedUtc","gte":"2026-01-11T00:00:00Z","lte":"2026-01-21T00:00:00Z"}""",
                luceneDateRange,
                [ids[1], ids[2]]);
            await AssertEquivalentAsync(
                "boolean term",
                """{"type":"term","field":"SearchTestArticle.Featured","value":true}""",
                """{"term":{"SearchTestArticle.Featured":"true"}}""",
                [ids[0], ids[2]]);
            await AssertEquivalentAsync(
                "multi-valued keyword term",
                JsonSerializer.Serialize(new { type = "term", field = DifferentialFieldsIndexHandler.MultiValueField, value = "member-20" }),
                JsonSerializer.Serialize(new { term = new Dictionary<string, string> { [DifferentialFieldsIndexHandler.MultiValueField] = "member-20" } }),
                [ids[1]],
                DifferentialFieldsIndexHandler.StoredField);
            await AssertEquivalentAsync(
                "keyword equality across values",
                JsonSerializer.Serialize(new { type = "term", field = DifferentialFieldsIndexHandler.MultiValueField, value = "sharedmember" }),
                JsonSerializer.Serialize(new { term = new Dictionary<string, string> { [DifferentialFieldsIndexHandler.MultiValueField] = "sharedmember" } }),
                [ids[0], ids[1]]);
            await AssertNullDifferenceAsync(ids[3]);
            await AssertEquivalentSearchAsync("Unicode term", "東京", [ids[0]]);
            await AssertEquivalentSearchAsync("punctuation term", "co-op", [ids[0]]);
            await AssertEquivalentSearchPagingAsync(ids);

            completed = true;

            async Task AssertEquivalentAsync(
                string name,
                string leanCorpusClause,
                string luceneClause,
                string[] expectedIds,
                string? storedField = null)
            {
                var request = new OrchardSearchTestHost.ProviderQueryRequest(
                    JsonSerializer.Serialize(new
                    {
                        index = "Search-LeanCorpus",
                        query = JsonSerializer.Deserialize<JsonElement>(leanCorpusClause),
                        take = 20,
                    }),
                    JsonSerializer.Serialize(new
                    {
                        query = JsonSerializer.Deserialize<JsonElement>(luceneClause),
                        size = 20,
                    }),
                    storedField);

                using HttpResponseMessage luceneResponse = await http.PostAsJsonAsync("/__test/query/Lucene/Search-Lucene", request);
                Assert.True(luceneResponse.IsSuccessStatusCode,
                    $"Lucene {name} query returned {(int)luceneResponse.StatusCode}: {await luceneResponse.Content.ReadAsStringAsync()}");
                using HttpResponseMessage leanCorpusResponse = await http.PostAsJsonAsync("/__test/query/LeanCorpus/Search-LeanCorpus", request);
                Assert.True(leanCorpusResponse.IsSuccessStatusCode,
                    $"LeanCorpus {name} query returned {(int)leanCorpusResponse.StatusCode}: {await leanCorpusResponse.Content.ReadAsStringAsync()}");

                OrchardSearchTestHost.ProviderQuerySnapshot lucene = await luceneResponse.Content
                    .ReadFromJsonAsync<OrchardSearchTestHost.ProviderQuerySnapshot>()
                    ?? throw new InvalidOperationException($"Orchard returned no Lucene {name} query result.");
                OrchardSearchTestHost.ProviderQuerySnapshot leanCorpus = await leanCorpusResponse.Content
                    .ReadFromJsonAsync<OrchardSearchTestHost.ProviderQuerySnapshot>()
                    ?? throw new InvalidOperationException($"Orchard returned no LeanCorpus {name} query result.");

                string[] expected = expectedIds.Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(expected, lucene.ContentItemIds.Order(StringComparer.Ordinal));
                Assert.Equal(expected, leanCorpus.ContentItemIds.Order(StringComparer.Ordinal));
                if (storedField is not null)
                {
                    string[] expectedStoredValues = expected.Select(id => $"stored-{id}").Order(StringComparer.Ordinal).ToArray();
                    Assert.Equal(expectedStoredValues, lucene.StoredFieldValues.Order(StringComparer.Ordinal));
                    Assert.Equal(expectedStoredValues, leanCorpus.StoredFieldValues.Order(StringComparer.Ordinal));
                }
                Console.WriteLine($"DIFFERENTIAL {name}: expected={expected.Length}; Lucene=passed; LeanCorpus=passed");
            }

            async Task AssertEquivalentSearchAsync(string name, string term, string[] expectedIds)
            {
                OrchardSearchTestHost.SearchSnapshot lucene = await http.GetFromJsonAsync<OrchardSearchTestHost.SearchSnapshot>(
                    $"/__test/search/Lucene/Search-Lucene?terms={Uri.EscapeDataString(term)}&start=0&size=10")
                    ?? throw new InvalidOperationException($"Orchard returned no Lucene {name} search result.");
                OrchardSearchTestHost.SearchSnapshot leanCorpus = await http.GetFromJsonAsync<OrchardSearchTestHost.SearchSnapshot>(
                    $"/__test/search/LeanCorpus/Search-LeanCorpus?terms={Uri.EscapeDataString(term)}&start=0&size=10")
                    ?? throw new InvalidOperationException($"Orchard returned no LeanCorpus {name} search result.");

                Assert.True(lucene.Success);
                Assert.True(leanCorpus.Success);
                string[] expected = expectedIds.Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(expected, lucene.ContentItemIds.Order(StringComparer.Ordinal));
                Assert.Equal(expected, leanCorpus.ContentItemIds.Order(StringComparer.Ordinal));
                Console.WriteLine($"DIFFERENTIAL {name}: expected={expected.Length}; Lucene=passed; LeanCorpus=passed");
            }

            async Task AssertNullDifferenceAsync(string expectedNullId)
            {
                var request = new OrchardSearchTestHost.ProviderQueryRequest(
                    """{"index":"Search-LeanCorpus","query":{"type":"isNull","field":"SearchTestArticle.Category"},"take":20}""",
                    """{"query":{"term":{"SearchTestArticle.Category":"NULL"}},"size":20}""");
                using HttpResponseMessage luceneResponse = await http.PostAsJsonAsync("/__test/query/Lucene/Search-Lucene", request);
                luceneResponse.EnsureSuccessStatusCode();
                using HttpResponseMessage leanCorpusResponse = await http.PostAsJsonAsync("/__test/query/LeanCorpus/Search-LeanCorpus", request);
                leanCorpusResponse.EnsureSuccessStatusCode();
                OrchardSearchTestHost.ProviderQuerySnapshot lucene = await luceneResponse.Content
                    .ReadFromJsonAsync<OrchardSearchTestHost.ProviderQuerySnapshot>()
                    ?? throw new InvalidOperationException("Orchard returned no Lucene null query result.");
                OrchardSearchTestHost.ProviderQuerySnapshot leanCorpus = await leanCorpusResponse.Content
                    .ReadFromJsonAsync<OrchardSearchTestHost.ProviderQuerySnapshot>()
                    ?? throw new InvalidOperationException("Orchard returned no LeanCorpus null query result.");

                Assert.Empty(lucene.ContentItemIds);
                Assert.Equal([expectedNullId], leanCorpus.ContentItemIds);
                Console.WriteLine("DIFFERENTIAL explicit null: Lucene omitted null field; LeanCorpus returned its explicit null marker");
            }

            async Task AssertEquivalentSearchPagingAsync(string[] expectedIds)
            {
                var luceneFirst = await GetSearchAsync("Lucene", "differential", start: 0, size: 2);
                var luceneSecond = await GetSearchAsync("Lucene", "differential", start: 2, size: 4);
                var leanCorpusFirst = await GetSearchAsync("LeanCorpus", "differential", start: 0, size: 2);
                var leanCorpusSecond = await GetSearchAsync("LeanCorpus", "differential", start: 2, size: 2);

                Assert.Equal(2, luceneFirst.ContentItemIds.Length);
                Assert.Equal(2, luceneSecond.ContentItemIds.Length);
                Assert.Equal(2, leanCorpusFirst.ContentItemIds.Length);
                Assert.Equal(2, leanCorpusSecond.ContentItemIds.Length);
                Assert.Equal(4, leanCorpusFirst.TotalCount);
                Assert.Equal(4, leanCorpusSecond.TotalCount);
                Assert.Equal(expectedIds.Order(StringComparer.Ordinal), luceneFirst.ContentItemIds
                    .Concat(luceneSecond.ContentItemIds).Order(StringComparer.Ordinal));
                Assert.Equal(expectedIds.Order(StringComparer.Ordinal), leanCorpusFirst.ContentItemIds
                    .Concat(leanCorpusSecond.ContentItemIds).Order(StringComparer.Ordinal));
                Console.WriteLine("DIFFERENTIAL paging/count: both providers returned the same four IDs across two pages; LeanCorpus reported total=4");
            }

            async Task<OrchardSearchTestHost.SearchSnapshot> GetSearchAsync(string provider, string term, int start, int size)
                => await http.GetFromJsonAsync<OrchardSearchTestHost.SearchSnapshot>(
                    $"/__test/search/{provider}/Search-{provider}?terms={Uri.EscapeDataString(term)}&start={start}&size={size}")
                    ?? throw new InvalidOperationException($"Orchard returned no {provider} paged search result.");
        }
        finally
        {
            await OrchardSearchTestHost.StopIsolatedAsync(app);
            if (completed && Directory.Exists(root))
                await OrchardSearchTestHost.DeleteContentRootAsync(root);
            else if (Directory.Exists(root))
                Console.WriteLine($"Orchard differential content root retained at {root}");
        }
    }
}
