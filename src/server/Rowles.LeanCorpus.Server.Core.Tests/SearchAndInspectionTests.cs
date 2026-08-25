using System.Text.Json;
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

    private static string TemporaryRoot() => Path.Combine(Path.GetTempPath(), $"lean-corpus-server-search-{Guid.NewGuid():N}");
    private static void DeleteRoot(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
