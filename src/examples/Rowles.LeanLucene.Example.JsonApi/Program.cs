using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Rowles.LeanLucene.Example.JsonApi;
using Rowles.LeanLucene.Example.JsonApi.Models;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Suggestions;

var builder = WebApplication.CreateBuilder(args);

string dataPath = builder.Configuration["LEANLUCENE_DATA_PATH"]
    ?? Environment.GetEnvironmentVariable("LEANLUCENE_DATA_PATH")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "data");

builder.Services.AddSingleton(sp =>
    new CollectionManager(dataPath, sp.GetRequiredService<ILogger<CollectionManager>>()));

var app = builder.Build();

var jsonOpts = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
};

static IResult? ValidateCollectionName(string name) =>
    CollectionManager.IsValidCollectionName(name)
        ? null
        : Results.BadRequest(new { error = "Collection names may contain only ASCII letters, digits, underscores, and hyphens." });

// ── Collections ─────────────────────────────────────────────────────────────

app.MapGet("/collections", (CollectionManager mgr) =>
{
    var list = mgr.ListCollections()
        .Select(c => new CollectionInfo { Name = c.Name, DocCount = c.DocCount })
        .ToList();
    return Results.Ok(list);
});

app.MapDelete("/collections/{name}", (string name, CollectionManager mgr) =>
{
    if (ValidateCollectionName(name) is { } invalidName) return invalidName;

    bool dropped = mgr.DropCollection(name);
    return dropped ? Results.Ok(new { message = $"Collection '{name}' deleted." })
                   : Results.NotFound(new { message = $"Collection '{name}' not found." });
});

// ── Documents ────────────────────────────────────────────────────────────────

app.MapPost("/collections/{name}/documents", async (
    string name,
    HttpRequest request,
    CollectionManager mgr) =>
{
    if (ValidateCollectionName(name) is { } invalidName) return invalidName;

    JsonElement payload;
    try
    {
        payload = await JsonSerializer.DeserializeAsync<JsonElement>(request.Body);
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = "Invalid JSON.", detail = ex.Message });
    }

    var writer = mgr.GetWriter(name);
    int count = 0;

    if (payload.ValueKind == JsonValueKind.Array)
    {
        foreach (var item in payload.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            writer.AddDocument(JsonMapper.ToDocument(item, item.GetRawText()));
            count++;
        }
    }
    else if (payload.ValueKind == JsonValueKind.Object)
    {
        writer.AddDocument(JsonMapper.ToDocument(payload, payload.GetRawText()));
        count = 1;
    }
    else
    {
        return Results.BadRequest(new { error = "Expected a JSON object or array of objects." });
    }

    mgr.CommitAndRefresh(name);
    return Results.Ok(new IndexResponse { Indexed = count });
});

app.MapDelete("/collections/{name}/documents", (
    string name,
    [FromQuery] string field,
    [FromQuery] string term,
    CollectionManager mgr) =>
{
    if (ValidateCollectionName(name) is { } invalidName) return invalidName;

    if (!mgr.Exists(name))
        return Results.NotFound(new { message = $"Collection '{name}' not found." });

    if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(term))
        return Results.BadRequest(new { error = "Query parameters 'field' and 'term' are required." });

    var writer = mgr.GetWriter(name);
    using var beforeLease = mgr.AcquireSearcher(name);
    var searcher = beforeLease.Searcher;
    int before = searcher.Stats.LiveDocCount;

    writer.DeleteDocuments(new TermQuery(field, term));
    mgr.CommitAndRefresh(name);

    using var afterLease = mgr.AcquireSearcher(name);
    int after = afterLease.Searcher.Stats.LiveDocCount;
    return Results.Ok(new DeleteResponse { Deleted = Math.Max(0, before - after) });
});

// ── Search ───────────────────────────────────────────────────────────────────

app.MapGet("/collections/{name}/search", (
    string name,
    [FromQuery] string q,
    [FromQuery] string? field,
    [FromQuery] int topN,
    CollectionManager mgr) =>
{
    if (ValidateCollectionName(name) is { } invalidName) return invalidName;

    if (!mgr.Exists(name))
        return Results.NotFound(new { message = $"Collection '{name}' not found." });

    if (string.IsNullOrWhiteSpace(q))
        return Results.BadRequest(new { error = "Query parameter 'q' is required." });

    topN = Math.Clamp(topN == 0 ? 10 : topN, 1, 100);
    string defaultField = string.IsNullOrEmpty(field) ? "content" : field;

    using var lease = mgr.AcquireSearcher(name);
    var searcher = lease.Searcher;
    var results = searcher.Search(q, defaultField, topN);

    var hits = results.ScoreDocs.Select(sd =>
    {
        var stored = searcher.GetStoredFields(sd.DocId);
        return new SearchHit
        {
            Score = sd.Score,
            Fields = JsonMapper.ToResultFields(stored)
        };
    }).ToList();

    // DidYouMean: suggest for each whitespace-separated token
    var suggestions = new List<string>();
    foreach (string token in q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        var candidates = DidYouMeanSuggester.Suggest(searcher, defaultField, token, maxEdits: 2, topN: 3);
        suggestions.AddRange(candidates
            .Where(s => !s.Term.Equals(token, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Term));
    }

    return Results.Ok(new SearchResponse
    {
        TotalHits = results.TotalHits,
        Hits = hits,
        Suggestions = suggestions.Distinct().ToList()
    });
});

app.Run();
