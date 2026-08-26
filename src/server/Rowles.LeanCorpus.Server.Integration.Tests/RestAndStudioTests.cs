using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Integration.Tests;

[Trait("Area", "Server")]
public sealed class RestAndStudioTests
{
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
            consistency = 3,
            readToken = writeToken
        };
        using JsonDocument searched = await SendSuccessAsync(client, HttpMethod.Post, "/v1/indices/books/search", search);
        Assert.Equal("one", searched.RootElement.GetProperty("value").GetProperty("hits")[0].GetProperty("documentId").GetString());
        Assert.True(searched.RootElement.GetProperty("value").GetProperty("timing").GetProperty("tookMilliseconds").GetInt64() >= 0);

        using JsonDocument explained = await SendSuccessAsync(client, HttpMethod.Post, "/v1/indices/books/explain", new { documentId = "one", query = new { kind = "term", field = "isbn", value = "978-1" } });
        Assert.True(explained.RootElement.GetProperty("value").GetProperty("isMatch").GetBoolean());

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
