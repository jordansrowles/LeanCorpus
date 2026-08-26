using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class SearchAndInspectionTests
{
    [Fact]
    public async Task SortingSearchAfterFacetsAndProjectionAreApplied()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await CreatePopulatedServer(root);
            SearchRequest firstRequest = new(
                new TermQueryDefinition("category", "guide"),
                Size: 1,
                Sort: [new SortDefinition("year", SortDirection.Ascending)],
                Facets: null,
                IncludeDocuments: false);
            SearchResponse first = (await server.SearchAsync("books", firstRequest)).Value!;
            Assert.Single(first.Hits);
            Assert.Equal("old", first.Hits[0].DocumentId);
            Assert.Null(first.Hits[0].Document);
            Assert.NotNull(first.NextSearchAfter);

            SearchResponse second = (await server.SearchAsync("books", firstRequest with { SearchAfter = first.NextSearchAfter })).Value!;
            Assert.Equal("new", Assert.Single(second.Hits).DocumentId);

            SearchResponse faceted = (await server.SearchAsync("books", new SearchRequest(
                new QueryStringDefinition("guide"),
                Facets: [new FacetDefinition("categories", "category", FacetKind.Terms, 10)]))).Value!;
            Assert.Equal(2, faceted.TotalHits);
            Assert.Equal(2, Assert.Single(Assert.Single(faceted.Facets!).Buckets).Count);
            Assert.True(faceted.Timing.TookMilliseconds >= 0);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task SearchValidationAndUnsupportedProjectionReturnTypedFailures()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await CreatePopulatedServer(root);
            Assert.Equal("invalid_query_field", (await server.SearchAsync("books", new SearchRequest(new TermQueryDefinition("missing", "value")))).Failure?.Code);
            Assert.Equal("invalid_search_request", (await server.SearchAsync("books", new SearchRequest(new QueryStringDefinition("guide"), Size: 101))).Failure?.Code);
            Assert.Equal("highlights_not_supported", (await server.SearchAsync("books", new SearchRequest(new QueryStringDefinition("guide"), IncludeHighlights: true))).Failure?.Code);
            Assert.Equal("explain_not_supported", (await server.ExplainAsync("books", new ExplainRequest("old", new PhraseQueryDefinition("title", ["old", "guide"])))).Failure?.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DefaultScorePaginationNoDuplicatesOrGaps()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await CreateSortableServer(root);
            IReadOnlyList<string> ids = await CollectAllIds(server, new SearchRequest(new QueryStringDefinition("guide"), Size: 2));

            Assert.Equal(["a", "b", "c", "d", "e", "f"], ids);
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DefaultScoreTieUsesStableIdAcrossRefresh()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await CreateSortableServer(root);
            SearchRequest request = new(new QueryStringDefinition("guide"), Size: 1);
            ServiceResult<SearchResponse> firstResult = await server.SearchAsync("books", request);
            Assert.True(firstResult.IsSuccess, firstResult.Failure?.Code);
            SearchResponse first = firstResult.Value!;
            Assert.Equal("a", Assert.Single(first.Hits).DocumentId);
            Assert.Equal("a", Assert.IsType<string>(first.Hits[0].SortValues![1]));
            Assert.IsType<double>(first.NextSearchAfter![2]);

            Assert.True((await server.RefreshAsync(new RefreshIndexRequest("books"))).IsSuccess);
            ServiceResult<SearchResponse> secondResult = await server.SearchAsync("books", request with { SearchAfter = first.NextSearchAfter });
            Assert.True(secondResult.IsSuccess, secondResult.Failure?.Code);
            Assert.Equal("b", Assert.Single(secondResult.Value!.Hits).DocumentId);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task TypedSortPaginationSupportsKeywordInt64DoubleAndDateTimeInBothDirections()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await CreateSortableServer(root);
            (string Field, string[] Ascending, string[] Descending)[] cases =
            [
                ("category", ["b", "e", "a", "c", "d", "f"], ["d", "f", "a", "c", "b", "e"]),
                ("number", ["b", "e", "a", "d", "c", "f"], ["c", "f", "a", "d", "b", "e"]),
                ("ratio", ["d", "f", "a", "c", "b", "e"], ["b", "e", "c", "a", "d", "f"]),
                ("when", ["b", "e", "a", "c", "d", "f"], ["d", "f", "c", "a", "b", "e"])
            ];

            foreach ((string field, string[] ascending, string[] descending) in cases)
            {
                IReadOnlyList<string> actualAscending = await CollectAllIds(
                    server,
                    new SearchRequest(new QueryStringDefinition("guide"), Size: 2,
                        Sort: [new SortDefinition(field, SortDirection.Ascending)]));
                IReadOnlyList<string> actualDescending = await CollectAllIds(
                    server,
                    new SearchRequest(new QueryStringDefinition("guide"), Size: 2,
                        Sort: [new SortDefinition(field, SortDirection.Descending)]));

                Assert.Equal(ascending, actualAscending);
                Assert.Equal(descending, actualDescending);
            }
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task InvalidSearchAfterLengthTypeAndSortShapeReturnTypedFailures()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await CreateSortableServer(root);
            SearchRequest request = new(
                new QueryStringDefinition("guide"),
                Size: 1,
                Sort: [new SortDefinition("category", SortDirection.Ascending)]);
            ServiceResult<SearchResponse> firstResult = await server.SearchAsync("books", request);
            Assert.True(firstResult.IsSuccess, firstResult.Failure?.Code);
            IReadOnlyList<object?> cursor = firstResult.Value!.NextSearchAfter!;

            ServiceResult<SearchResponse> wrongLength = await server.SearchAsync("books", request with { SearchAfter = cursor.Take(cursor.Count - 1).ToArray() });
            Assert.Equal("invalid_search_after", wrongLength.Failure?.Code);

            object?[] wrongType = cursor.ToArray();
            wrongType[2] = 42L;
            ServiceResult<SearchResponse> wrongTypeResult = await server.SearchAsync("books", request with { SearchAfter = wrongType });
            Assert.Equal("invalid_search_after", wrongTypeResult.Failure?.Code);

            ServiceResult<SearchResponse> wrongShape = await server.SearchAsync(
                "books",
                request with
                {
                    Sort = [new SortDefinition("category", SortDirection.Descending)],
                    SearchAfter = cursor
                });
            Assert.Equal("invalid_search_after", wrongShape.Failure?.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task CommunityInspectionResourcesAreBoundedAndUnsupportedResourcesAreTyped()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await CreatePopulatedServer(root);
            foreach (InspectionResource resource in new[]
            {
                InspectionResource.IndexInventory,
                InspectionResource.Storage,
                InspectionResource.ReaderState,
                InspectionResource.Fields,
                InspectionResource.Segments,
                InspectionResource.Documents
            })
            {
                Assert.True((await server.InspectAsync("books", new InspectionRequest(resource, 10))).IsSuccess, resource.ToString());
            }

            Assert.Equal("inspection_not_supported", (await server.InspectAsync("books", new InspectionRequest(InspectionResource.Postings, 10))).Failure?.Code);
            Assert.Equal("invalid_inspection_limit", (await server.InspectAsync("books", new InspectionRequest(InspectionResource.Documents, 101))).Failure?.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static async ValueTask<LocalServerCore> CreatePopulatedServer(string root)
    {
        LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 100, MaximumInspectionItems = 100 });
        CreateIndexRequest request = new(
            "books",
            new IndexSchema(
            [
                new IndexFieldDefinition("title", IndexFieldType.Text, true, true),
                new IndexFieldDefinition("category", IndexFieldType.Keyword, true, true),
                new IndexFieldDefinition("year", IndexFieldType.Int64, true, true)
            ], new Dictionary<string, AnalysisDefinition>()),
            new IndexTopologySettings(1, 0),
            new MutableIndexSettings(null, null, "title", null));
        Assert.True((await server.CreateAsync(request)).IsSuccess);
        using JsonDocument old = JsonDocument.Parse("{\"title\":\"old guide\",\"category\":\"guide\",\"year\":2001}");
        using JsonDocument recent = JsonDocument.Parse("{\"title\":\"new guide\",\"category\":\"guide\",\"year\":2026}");
        Assert.True((await server.BulkAsync(new BulkDocumentsRequest("books",
        [
            new BulkDocumentOperation(DocumentOperationKind.Index, "old", old.RootElement.Clone()),
            new BulkDocumentOperation(DocumentOperationKind.Index, "new", recent.RootElement.Clone())
        ], Refresh: true))).IsSuccess);
        return server;
    }

    private static async ValueTask<LocalServerCore> CreateSortableServer(string root)
    {
        LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 100 });
        CreateIndexRequest request = new(
            "books",
            new IndexSchema(
            [
                new IndexFieldDefinition("title", IndexFieldType.Text, true, true),
                new IndexFieldDefinition("category", IndexFieldType.Keyword, true, true),
                new IndexFieldDefinition("number", IndexFieldType.Int64, true, true),
                new IndexFieldDefinition("ratio", IndexFieldType.Double, true, true),
                new IndexFieldDefinition("when", IndexFieldType.DateTime, true, true)
            ], new Dictionary<string, AnalysisDefinition>()),
            new IndexTopologySettings(1, 0),
            new MutableIndexSettings(null, null, "title", null));
        Assert.True((await server.CreateAsync(request)).IsSuccess);

        using JsonDocument source = JsonDocument.Parse(
            """
            [
              {"id":"a","document":{"title":"guide","category":"beta","number":2,"ratio":1.5,"when":"2026-01-02T00:00:00Z"}},
              {"id":"b","document":{"title":"guide","category":"alpha","number":1,"ratio":3.0,"when":"2026-01-01T00:00:00Z"}},
              {"id":"c","document":{"title":"guide","category":"beta","number":3,"ratio":2.0,"when":"2026-01-03T00:00:00Z"}},
              {"id":"d","document":{"title":"guide","category":"gamma","number":2,"ratio":0.5,"when":"2026-01-04T00:00:00Z"}},
              {"id":"e","document":{"title":"guide","category":"alpha","number":1,"ratio":3.0,"when":"2026-01-01T00:00:00Z"}},
              {"id":"f","document":{"title":"guide","category":"gamma","number":3,"ratio":0.5,"when":"2026-01-04T00:00:00Z"}}
            ]
            """);
        List<BulkDocumentOperation> operations = [];
        foreach (JsonElement item in source.RootElement.EnumerateArray())
            operations.Add(new BulkDocumentOperation(
                DocumentOperationKind.Index,
                item.GetProperty("id").GetString()!,
                item.GetProperty("document").Clone()));

        Assert.True((await server.BulkAsync(new BulkDocumentsRequest("books", operations, Refresh: true))).IsSuccess);
        return server;
    }

    private static async Task<IReadOnlyList<string>> CollectAllIds(LocalServerCore server, SearchRequest request)
    {
        List<string> ids = [];
        SearchRequest pageRequest = request;
        for (int page = 0; page < 10; page++)
        {
            ServiceResult<SearchResponse> result = await server.SearchAsync("books", pageRequest);
            Assert.True(result.IsSuccess, result.Failure?.Code);
            SearchResponse response = result.Value!;
            if (response.Hits.Count == 0)
                return ids;

            ids.AddRange(response.Hits.Select(hit => hit.DocumentId));
            Assert.NotNull(response.NextSearchAfter);
            pageRequest = pageRequest with { SearchAfter = response.NextSearchAfter };
        }

        Assert.Fail($"Search-after pagination did not terminate: {string.Join(',', ids)}");
        return ids;
    }

    private static string TemporaryRoot() => Path.Combine(Path.GetTempPath(), $"lean-corpus-server-search-{Guid.NewGuid():N}");
    private static void DeleteRoot(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
