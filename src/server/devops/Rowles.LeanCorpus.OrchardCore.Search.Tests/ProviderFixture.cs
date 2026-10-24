using Microsoft.Extensions.Logging.Abstractions;
using OrchardCore.Indexing.Models;
using Rowles.LeanCorpus.OrchardCore.Search.Indexing;
using Rowles.LeanCorpus.OrchardCore.Search.Search;
using Rowles.LeanCorpus.OrchardCore.Search.Storage;

namespace Rowles.LeanCorpus.OrchardCore.Search.Tests;

internal sealed class ProviderFixture : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "leancorpus-orchard-tests", Guid.NewGuid().ToString("N"));
    private LeanCorpusIndexHandleCache _handles;

    public SwitchableFailureInjector Failures { get; } = new();
    internal object RootHostContext { get; } = new();
    public LeanCorpusIndexPathResolver Paths { get; }
    public LeanCorpusSchemaStore Schemas { get; }
    public LeanCorpusDocumentMapper Mapper { get; }
    public LeanCorpusIndexNameProvider Names { get; } = new("Default");
    internal LeanCorpusIndexHandleCache Handles => _handles;
    public LeanCorpusIndexManager Indexes { get; private set; }
    public LeanCorpusDocumentIndexManager Documents { get; private set; }
    public LeanCorpusSearchService Search { get; private set; }
    public IndexProfile Profile { get; } = new();

    public ProviderFixture()
    {
        Directory.CreateDirectory(_root);
        Paths = new LeanCorpusIndexPathResolver(_root, "Default");
        _handles = new LeanCorpusIndexHandleCache(Paths, lifetime: null, manualRootLifetimeContext: RootHostContext);
        Schemas = new LeanCorpusSchemaStore(Failures);
        Mapper = new LeanCorpusDocumentMapper(NullLogger<LeanCorpusDocumentMapper>.Instance);
        Indexes = new LeanCorpusIndexManager(Paths, _handles, Schemas, Failures, NullLogger<LeanCorpusIndexManager>.Instance);
        Documents = new LeanCorpusDocumentIndexManager(Paths, _handles, Schemas, Mapper, Failures,
            NullLogger<LeanCorpusDocumentIndexManager>.Instance);
        Search = new LeanCorpusSearchService(Paths, _handles, Schemas, new LeanCorpusSearchCompiler(),
            NullLogger<LeanCorpusSearchService>.Instance);
        Profile.IndexFullName = Names.GetFullIndexName("Search");
    }

    public async ValueTask DisposeAsync()
    {
        await _handles.DisposeAsync();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public async Task RestartAsync()
    {
        await _handles.DisposeAsync();
        _handles = new LeanCorpusIndexHandleCache(Paths, lifetime: null, manualRootLifetimeContext: RootHostContext);
        Failures.Point = null;
        Indexes = new LeanCorpusIndexManager(Paths, _handles, Schemas, Failures, NullLogger<LeanCorpusIndexManager>.Instance);
        Documents = new LeanCorpusDocumentIndexManager(Paths, _handles, Schemas, Mapper, Failures,
            NullLogger<LeanCorpusDocumentIndexManager>.Instance);
        Search = new LeanCorpusSearchService(Paths, _handles, Schemas, new LeanCorpusSearchCompiler(),
            NullLogger<LeanCorpusSearchService>.Instance);
    }

    public async Task<string[]> SearchCompiledAsync(Rowles.LeanCorpus.Search.Query query, int topN)
    {
        LeanCorpusIndexPaths paths = Paths.Resolve(Profile.IndexFullName);
        LeanCorpusSchemaManifest schema = await Schemas.ReadAsync(paths.SchemaPath);
        return await _handles.WithSearcherAsync(paths, searcher =>
        {
            var hits = searcher.Search(query, topN);
            return hits.ScoreDocs.Select(hit =>
            {
                var stored = searcher.GetStoredFields(hit.DocId);
                return stored.TryGetValue(LeanCorpusDocumentMapper.ContentItemIdField, out var ids)
                    ? ids.FirstOrDefault() ?? string.Empty
                    : string.Empty;
            }).Where(id => id.Length > 0).ToArray();
        }, schema);
    }
}

internal sealed class SwitchableFailureInjector : ILeanCorpusFailureInjector
{
    public LeanCorpusFailurePoint? Point { get; set; }

    public void Check(LeanCorpusFailurePoint point)
    {
        if (Point == point)
        {
            Point = null;
            throw new IOException($"Injected failure at {point}.");
        }
    }
}
