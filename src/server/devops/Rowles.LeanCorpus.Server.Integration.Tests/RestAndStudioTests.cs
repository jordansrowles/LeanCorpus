using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Integration.Tests;

[Trait("Area", "Server")]
public sealed class RestAndStudioTests
{
    [Fact]
    public async Task DataForgeCorpusCanBeIndexedAndSearchedOverRest()
    {
        var profile = new LeanCorpusE2eProfile();
        var options = new DataForgeGenerationOptions(42, 256);
        var records = profile.Generate(options).ToArray();
        var contentHash = DataForgeMaterialiser.GenerateToStream(profile, options, Stream.Null).ContentSha256;
        var replay = $"DataForge leancorpus-e2e/v1 seed=42 count=256 content={contentHash}";
        Console.WriteLine(replay);

        await using ServerHostScope host = await ServerHostScope.StartAsync(HttpProtocols.Http1);
        using HttpClient client = host.CreateHttpClient();
        using (await SendSuccessAsync(client, HttpMethod.Put, "/v1/indices/forge-e2e", new
        {
            indexName = "forge-e2e",
            schema = new
            {
                fields = new[]
                {
                    new { name = "title", type = 0, indexed = true, stored = true, multiValued = false, analyser = (string?)"standard" },
                    new { name = "body", type = 0, indexed = true, stored = true, multiValued = false, analyser = (string?)"standard" },
                    new { name = "category", type = 1, indexed = true, stored = true, multiValued = false, analyser = (string?)null },
                    new { name = "year", type = 2, indexed = true, stored = true, multiValued = false, analyser = (string?)null },
                    new { name = "price", type = 3, indexed = true, stored = true, multiValued = false, analyser = (string?)null },
                    new { name = "active", type = 4, indexed = true, stored = true, multiValued = false, analyser = (string?)null }
                },
                analysis = new Dictionary<string, object>()
            },
            topology = new { shardCount = 1, replicaCount = 0 },
            settings = new { refreshInterval = (string?)null, commitInterval = (string?)null, defaultField = "body", maximumQueryClauses = (int?)null }
        })) { }

        var operations = records.Select(static record => new
        {
            kind = 0,
            documentId = record.Id,
            document = new
            {
                title = record.Title,
                body = record.Body,
                category = record.Category,
                year = record.Year,
                price = record.PriceMinor / 100d,
                active = record.Active
            }
        }).ToArray();
        foreach (var batch in operations.Chunk(100))
        {
            using JsonDocument indexed = await SendSuccessAsync(client, HttpMethod.Post,
                "/v1/indices/forge-e2e/documents:bulk", new { indexName = "forge-e2e", operations = batch, refresh = true });
            JsonElement items = indexed.RootElement.GetProperty("value").GetProperty("items");
            Assert.True(items.GetArrayLength() == batch.Length &&
                items.EnumerateArray().All(static item => item.GetProperty("accepted").GetBoolean()),
                $"{replay}: a bulk batch did not accept all its records.");
        }

        using (JsonDocument exact = await SearchAsync(new { query = new { kind = "queryString", text = "forgeexactanchor", defaultField = "body" }, size = 10 }))
            Assert.True(HitIds(exact).SequenceEqual(["e2e-exact"]), $"{replay}: exact anchor search did not return only e2e-exact.");

        using (JsonDocument phrase = await SearchAsync(new { query = new { kind = "queryString", text = "\"deterministic corpus replay\"", defaultField = "body" }, size = 20 }))
            Assert.True(HitIds(phrase).Contains("e2e-phrase", StringComparer.Ordinal), $"{replay}: phrase query did not include e2e-phrase.");

        using (JsonDocument filtered = await SearchAsync(new { query = new { kind = "term", field = "category", value = "scenario-filter" }, size = 10 }))
            Assert.True(HitIds(filtered).SequenceEqual(["e2e-filter"]), $"{replay}: keyword term query did not return e2e-filter.");

        using (JsonDocument unicode = await SearchAsync(new { query = new { kind = "term", field = "category", value = "scenario-unicode" }, size = 10, includeDocuments = true }))
        {
            var hit = Assert.Single(unicode.RootElement.GetProperty("value").GetProperty("hits").EnumerateArray());
            Assert.True(hit.GetProperty("documentId").GetString() == "e2e-unicode" &&
                hit.GetProperty("document").GetProperty("body").GetString() == "café naïve Ελληνικά 日本語 العربية",
                $"{replay}: stored Unicode document did not round trip.");
        }

        using (JsonDocument faceted = await SearchAsync(new
        {
            query = new { kind = "wildcard", field = "category", pattern = "*" },
            size = 10,
            facets = new[] { new { name = "categories", field = "category", kind = 0, size = 64 } }
        }))
        {
            var categories = faceted.RootElement.GetProperty("value").GetProperty("facets")[0]
                .GetProperty("buckets").EnumerateArray()
                .Select(static bucket => bucket.GetProperty("key").GetString()).ToHashSet(StringComparer.Ordinal);
            Assert.True(new[] { "scenario-exact", "scenario-phrase", "scenario-filter", "scenario-unicode", "scenario-long" }
                .All(categories.Contains), $"{replay}: category facet omitted a reserved scenario.");
        }

        Task<JsonDocument> SearchAsync(object request) =>
            SendSuccessAsync(client, HttpMethod.Post, "/v1/indices/forge-e2e/search", request);

        static string[] HitIds(JsonDocument response) => response.RootElement.GetProperty("value").GetProperty("hits")
            .EnumerateArray().Select(static hit => hit.GetProperty("documentId").GetString()!).ToArray();
    }

    [Fact]
    public async Task CommunityRestAndStudioScenarioIsComplete()
    {
        await using ServerHostScope host = await ServerHostScope.StartAsync(HttpProtocols.Http1);
        using HttpClient client = host.CreateHttpClient();

        using JsonDocument health = await GetSuccessAsync(client, "/v1/health");
        Assert.True(health.RootElement.GetProperty("value").GetProperty("isHealthy").GetBoolean());
        using JsonDocument readiness = await GetSuccessAsync(client, "/v1/ready");
        Assert.True(readiness.RootElement.GetProperty("value").GetProperty("isReady").GetBoolean());

        HttpResponseMessage studio = await client.GetAsync("/studio");
        Assert.Equal(HttpStatusCode.OK, studio.StatusCode);
        Assert.Contains("LeanCorpus Studio", await studio.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/studio/assets/studio.js")).StatusCode);

        object create = CreateIndexPayload("books");
        using JsonDocument created = await SendSuccessAsync(client, HttpMethod.Put, "/v1/indices/books", create);
        string indexId = created.RootElement.GetProperty("value").GetProperty("indexId").GetString()!;
        Assert.DoesNotContain("books", indexId, StringComparison.OrdinalIgnoreCase);

        HttpResponseMessage duplicate = await client.PutAsJsonAsync("/v1/indices/books", create);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        object bulk = new
        {
            indexName = "books",
            operations = new[] { new { kind = 0, documentId = "one", document = new { title = "Practical search", isbn = "978-1" } } },
            refresh = true,
            idempotencyKey = "write-1"
        };
        using JsonDocument indexed = await SendSuccessAsync(client, HttpMethod.Post, "/v1/indices/books/documents:bulk", bulk);
        Assert.True(indexed.RootElement.GetProperty("value").GetProperty("items")[0].GetProperty("accepted").GetBoolean());
        JsonElement writeToken = indexed.RootElement.GetProperty("value").GetProperty("writeToken").Clone();
        Assert.Equal(indexId, writeToken.GetProperty("indexId").GetString());

        object search = new
        {
            query = new { kind = "term", field = "isbn", value = "978-1" },
            size = 10,
            includeDocuments = true,
            consistency = "ReadYourWrites",
            readToken = writeToken
        };
        using JsonDocument searched = await SendSuccessAsync(client, HttpMethod.Post, "/v1/indices/books/search", search);
        Assert.Equal("one", searched.RootElement.GetProperty("value").GetProperty("hits")[0].GetProperty("documentId").GetString());
        Assert.True(searched.RootElement.GetProperty("value").GetProperty("timing").GetProperty("tookMilliseconds").GetInt64() >= 0);

        using JsonDocument explained = await SendSuccessAsync(client, HttpMethod.Post, "/v1/indices/books/explain", new { documentId = "one", query = new { kind = "term", field = "isbn", value = "978-1" } });
        Assert.True(explained.RootElement.GetProperty("value").GetProperty("isMatch").GetBoolean());

        using JsonDocument unsupportedExplain = await SendFailureAsync(client, HttpMethod.Post, "/v1/indices/books/explain",
            new { documentId = "one", query = new { kind = "phrase", field = "title", terms = new[] { "practical", "search" } } }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("explain_not_supported", unsupportedExplain.RootElement.GetProperty("failure").GetProperty("code").GetString());

        using JsonDocument unsupportedDurability = await SendFailureAsync(client, HttpMethod.Post, "/v1/indices/books/documents:bulk", new
        {
            indexName = "books",
            operations = new[] { new { kind = 0, documentId = "two", document = new { title = "Second", isbn = "978-2" } } },
            durability = "Quorum"
        }, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("durability_not_supported", unsupportedDurability.RootElement.GetProperty("failure").GetProperty("code").GetString());

        using JsonDocument unsupportedConsistency = await SendFailureAsync(client, HttpMethod.Post, "/v1/indices/books/search", new
        {
            query = new { kind = "term", field = "isbn", value = "978-1" },
            consistency = "Replica"
        }, HttpStatusCode.ServiceUnavailable);
        Assert.Equal("consistency_unavailable", unsupportedConsistency.RootElement.GetProperty("failure").GetProperty("code").GetString());

        using JsonDocument unsupportedInspection = await SendFailureAsync(client, HttpMethod.Get, "/v1/indices/books/inspection/terms", null, HttpStatusCode.UnprocessableEntity);
        Assert.Equal("inspection_not_supported", unsupportedInspection.RootElement.GetProperty("failure").GetProperty("code").GetString());

        foreach (string endpoint in new[] { "schema", "stats", "inspection/fields", "inspection/segments", "inspection/documents", "inspection/storage" })
            using (await GetSuccessAsync(client, $"/v1/indices/books/{endpoint}")) { }

        string settingsToken = ConfirmationTokens.Create("update-settings", "books");
        using HttpRequestMessage settingsRequest = new(HttpMethod.Patch, "/v1/indices/books/settings")
        {
            Content = JsonContent.Create(new { indexName = "books", settings = new { refreshInterval = "00:00:01", commitInterval = "00:00:05", defaultField = "title", maximumQueryClauses = 128 } })
        };
        settingsRequest.Headers.Add("X-LeanCorpus-Confirm", settingsToken);
        using HttpResponseMessage settingsResponse = await client.SendAsync(settingsRequest);
        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync("/v1/indices/books")).StatusCode);
        using HttpRequestMessage deleteRequest = new(HttpMethod.Delete, "/v1/indices/books");
        deleteRequest.Headers.Add("X-LeanCorpus-Confirm", ConfirmationTokens.Create("delete-index", "books"));
        using HttpResponseMessage deleted = await client.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
    }

    [Fact]
    public async Task CommittedDocumentsSurviveAHostRestart()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-restart-{Guid.NewGuid():N}");
        try
        {
            await using (ServerHostScope first = await ServerHostScope.StartAsync(HttpProtocols.Http1, root, deleteRoot: false))
            using (HttpClient client = first.CreateHttpClient())
            {
                using (await SendSuccessAsync(client, HttpMethod.Put, "/v1/indices/books", CreateIndexPayload("books"))) { }
                using (await SendSuccessAsync(client, HttpMethod.Post, "/v1/indices/books/documents:bulk", new
                {
                    indexName = "books",
                    operations = new[] { new { kind = 0, documentId = "one", document = new { title = "Restart proof", isbn = "one" } } },
                    refresh = true
                })) { }
            }

            await using ServerHostScope second = await ServerHostScope.StartAsync(HttpProtocols.Http1, root, deleteRoot: false);
            using HttpClient restarted = second.CreateHttpClient();
            using JsonDocument search = await SendSuccessAsync(restarted, HttpMethod.Post, "/v1/indices/books/search", new { query = new { kind = "term", field = "isbn", value = "one" } });
            Assert.Equal("one", search.RootElement.GetProperty("value").GetProperty("hits")[0].GetProperty("documentId").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static object CreateIndexPayload(string name) => new
    {
        indexName = name,
        schema = new
        {
            fields = new[]
            {
                new { name = "title", type = 0, indexed = true, stored = true, multiValued = false, analyser = (string?)"standard" },
                new { name = "isbn", type = 1, indexed = true, stored = true, multiValued = false, analyser = (string?)null }
            },
            analysis = new Dictionary<string, object>()
        },
        topology = new { shardCount = 1, replicaCount = 0 },
        settings = new { refreshInterval = (string?)null, commitInterval = (string?)null, defaultField = "title", maximumQueryClauses = (int?)null }
    };

    private static async Task<JsonDocument> GetSuccessAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await client.GetAsync(path);
        return await ReadSuccessAsync(response);
    }

    private static async Task<JsonDocument> SendSuccessAsync(HttpClient client, HttpMethod method, string path, object value)
    {
        using HttpRequestMessage request = new(method, path) { Content = JsonContent.Create(value) };
        using HttpResponseMessage response = await client.SendAsync(request);
        return await ReadSuccessAsync(response);
    }

    private static async Task<JsonDocument> SendFailureAsync(HttpClient client, HttpMethod method, string path, object? value, HttpStatusCode expectedStatus)
    {
        using HttpRequestMessage request = new(method, path);
        if (value is not null)
            request.Content = JsonContent.Create(value);
        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Equal("1", response.Headers.GetValues("X-API-Version").Single());
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("X-Request-ID").Single()));
        JsonDocument document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("isSuccess").GetBoolean());
        return document;
    }

    private static async Task<JsonDocument> ReadSuccessAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, body);
        Assert.Equal("1", response.Headers.GetValues("X-API-Version").Single());
        Assert.False(string.IsNullOrWhiteSpace(response.Headers.GetValues("X-Request-ID").Single()));
        JsonDocument document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.GetProperty("isSuccess").GetBoolean());
        return document;
    }
}
