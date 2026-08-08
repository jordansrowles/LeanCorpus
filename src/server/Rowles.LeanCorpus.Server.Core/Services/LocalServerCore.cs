using System.Text.Json;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Abstractions.Ports;
using Rowles.LeanCorpus.Server.Abstractions.Services;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Runtime;
using Rowles.LeanCorpus.Server.Core.Storage;

namespace Rowles.LeanCorpus.Server.Core.Services;

/// <summary>Implements the local index lifecycle and basic server health contracts.</summary>
public sealed class LocalServerCore : IIndexService, IHealthService, IDocumentService, ISearchService, IDisposable
{
    private readonly LocalIndexRegistry _registry;
    private readonly ServerCoreOptions _options;
    private readonly ServerPortSet _ports;
    private readonly DateTimeOffset _startedUtc;

    private LocalServerCore(LocalIndexRegistry registry, ServerCoreOptions options, ServerPortSet ports)
    {
        _registry = registry;
        _options = options;
        _ports = ports;
        _startedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Opens the local registry at the configured data root.</summary>
    public static async ValueTask<LocalServerCore> OpenAsync(ServerCoreOptions options, ServerPortSet? ports = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataRoot);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumBulkOperations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumSearchResults, 1);
        return new LocalServerCore(await LocalIndexRegistry.OpenAsync(Path.GetFullPath(options.DataRoot), cancellationToken).ConfigureAwait(false), options, ports ?? ServerPortSet.Community);
    }

    /// <inheritdoc />
    public ValueTask<ServiceResult<IReadOnlyList<IndexSummary>>> ListAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Success<IReadOnlyList<IndexSummary>>(_registry.List().Select(ToSummary).ToArray()));

    /// <inheritdoc />
    public async ValueTask<ServiceResult<IndexSummary>> CreateAsync(CreateIndexRequest request, CancellationToken cancellationToken = default)
    {
        OperationContext context = Context(OperationKind.CreateIndex, request.IndexName);
        if (!(await _ports.Authorisation.AuthoriseAsync(new OperationPermission(context, EndpointAccess.Administrative), cancellationToken).ConfigureAwait(false)).IsAllowed)
            return Failure<IndexSummary>("forbidden", "The caller is not authorised to create an index.");

        if (!IndexName.IsValid(request.IndexName))
            return Failure<IndexSummary>("invalid_index_name", "Index names may contain only ASCII letters, digits, underscores, and hyphens.");

        if (request.Schema.Fields.Count == 0 || request.Topology.ShardCount != 1 || request.Topology.ReplicaCount != 0)
            return Failure<IndexSummary>("invalid_schema", "Local Server requires a non-empty schema with one shard and no replicas.");

        IndexRegistration registration = new(request.IndexName, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, request.Schema, request.Topology, request.Settings, SchemaHash.Compute(request.Schema, request.Topology));
        await _ports.Lifecycle.OnTransitionAsync(new IndexLifecycleEvent(context, request.IndexName, IndexLifecycleStage.Creating), cancellationToken).ConfigureAwait(false);
        IndexRuntimeEntry? entry = await _registry.CreateAsync(registration, cancellationToken).ConfigureAwait(false);
        if (entry is not null)
            await _ports.Lifecycle.OnTransitionAsync(new IndexLifecycleEvent(context, request.IndexName, IndexLifecycleStage.Created), cancellationToken).ConfigureAwait(false);
        return entry is null
            ? Failure<IndexSummary>("index_exists", "An index with that name already exists.")
            : Success(ToSummary(entry));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<bool>> DeleteAsync(DeleteIndexRequest request, CancellationToken cancellationToken = default)
    {
        OperationContext context = Context(OperationKind.DeleteIndex, request.IndexName);
        if (!(await _ports.Authorisation.AuthoriseAsync(new OperationPermission(context, EndpointAccess.Administrative), cancellationToken).ConfigureAwait(false)).IsAllowed)
            return Failure<bool>("forbidden", "The caller is not authorised to delete an index.");

        if (!IndexName.IsValid(request.IndexName))
            return Failure<bool>("invalid_index_name", "The index name is invalid.");

        await _ports.Lifecycle.OnTransitionAsync(new IndexLifecycleEvent(context, request.IndexName, IndexLifecycleStage.Deleting), cancellationToken).ConfigureAwait(false);
        bool deleted = await _registry.DeleteAsync(request.IndexName, cancellationToken).ConfigureAwait(false);
        if (deleted)
            await _ports.Lifecycle.OnTransitionAsync(new IndexLifecycleEvent(context, request.IndexName, IndexLifecycleStage.Deleted), cancellationToken).ConfigureAwait(false);
        return deleted ? Success(true) : Failure<bool>("index_not_found", "The index does not exist.");
    }

    /// <inheritdoc />
    public ValueTask<ServiceResult<IndexSchemaResponse>> GetSchemaAsync(string indexName, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_registry.TryGet(indexName, out IndexRuntimeEntry? entry)
            ? Success(new IndexSchemaResponse(entry!.Registration.Name, entry.Registration.Schema, entry.Registration.SchemaHash, entry.Registration.Settings))
            : Failure<IndexSchemaResponse>("index_not_found", "The index does not exist."));

    /// <inheritdoc />
    public ValueTask<ServiceResult<IndexStatisticsResponse>> GetStatisticsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(indexName, out IndexRuntimeEntry? entry))
            return ValueTask.FromResult(Failure<IndexStatisticsResponse>("index_not_found", "The index does not exist."));

        using SearcherLease lease = entry!.Runtime.Searchers.AcquireLease();
        var statistics = new IndexStatisticsResponse(indexName, entry.Registration.SchemaHash, lease.Searcher.Stats.LiveDocCount, 0, DirectorySize(entry.Runtime.Path), 0, lease.CommitGeneration);
        return ValueTask.FromResult(Success(statistics));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<IndexSummary>> UpdateSettingsAsync(UpdateIndexSettingsRequest request, CancellationToken cancellationToken = default)
    {
        IndexRuntimeEntry? entry = await _registry.UpdateSettingsAsync(request.IndexName, request.Settings, cancellationToken).ConfigureAwait(false);
        return entry is null ? Failure<IndexSummary>("index_not_found", "The index does not exist.") : Success(ToSummary(entry));
    }

    /// <inheritdoc />
    public ValueTask<ServiceResult<RefreshIndexResponse>> RefreshAsync(RefreshIndexRequest request, CancellationToken cancellationToken = default)
    {
        if (!_registry.TryGet(request.IndexName, out IndexRuntimeEntry? entry))
            return ValueTask.FromResult(Failure<RefreshIndexResponse>("index_not_found", "The index does not exist."));

        entry!.Runtime.Searchers.MaybeRefresh();
        return ValueTask.FromResult(Success(new RefreshIndexResponse(request.IndexName, 0)));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<BulkDocumentsResponse>> BulkAsync(BulkDocumentsRequest request, CancellationToken cancellationToken = default)
    {
        OperationContext context = Context(OperationKind.WriteDocuments, request.IndexName);
        if (!(await _ports.Authorisation.AuthoriseAsync(new OperationPermission(context, EndpointAccess.Public), cancellationToken).ConfigureAwait(false)).IsAllowed)
            return Failure<BulkDocumentsResponse>("forbidden", "The caller is not authorised to write documents.");

        OperationRoute route = await _ports.Router.RouteAsync(new OperationRouteRequest(context, true), cancellationToken).ConfigureAwait(false);
        if (route.TargetKind is RouteTargetKind.Rejected or RouteTargetKind.Remote)
            return Failure<BulkDocumentsResponse>("route_unavailable", "The write cannot execute on this server.");

        if (!_registry.TryGet(request.IndexName, out IndexRuntimeEntry? entry))
            return Failure<BulkDocumentsResponse>("index_not_found", "The index does not exist.");

        if (request.Operations.Count is 0 or > 10_000)
            return Failure<BulkDocumentsResponse>("invalid_bulk_request", "The request contains an invalid number of document operations.");

        List<BulkDocumentResult> results = new(request.Operations.Count);
        foreach (BulkDocumentOperation operation in request.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.DocumentId))
            {
                results.Add(new BulkDocumentResult(operation.DocumentId, false, new ApiFailure("invalid_document_id", "Document IDs are required.")));
                continue;
            }

            if (operation.Kind is DocumentOperationKind.Index or DocumentOperationKind.Update)
            {
                if (operation.Document is not { ValueKind: JsonValueKind.Object } document)
                {
                    results.Add(new BulkDocumentResult(operation.DocumentId, false, new ApiFailure("invalid_document", "Index and update operations require a JSON object.")));
                    continue;
                }

                entry!.Runtime.Writer.UpdateDocument(ServerDocumentMapper.DocumentIdField, operation.DocumentId, ServerDocumentMapper.Map(operation.DocumentId, document));
                results.Add(new BulkDocumentResult(operation.DocumentId, true));
                continue;
            }

            entry!.Runtime.Writer.DeleteDocuments(new TermQuery(ServerDocumentMapper.DocumentIdField, operation.DocumentId));
            results.Add(new BulkDocumentResult(operation.DocumentId, true));
        }

        entry!.Runtime.Writer.Commit();
        if (request.Refresh)
            entry.Runtime.Searchers.MaybeRefresh();

        WriteAcknowledgement acknowledgement = await _ports.WriteAcknowledgements.AcknowledgeAsync(new WriteCommitState(context, request.IndexName, 0), cancellationToken).ConfigureAwait(false);
        await _ports.Audit.PublishAsync(new AuditEvent(context, acknowledgement.IsAcknowledged), cancellationToken).ConfigureAwait(false);
        return Success(new BulkDocumentsResponse(results, acknowledgement.IsAcknowledged, null));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<SearchResponse>> SearchAsync(string indexName, SearchRequest request, CancellationToken cancellationToken = default)
    {
        OperationContext context = Context(OperationKind.Search, indexName);
        if (!(await _ports.Authorisation.AuthoriseAsync(new OperationPermission(context, EndpointAccess.Public), cancellationToken).ConfigureAwait(false)).IsAllowed)
            return Failure<SearchResponse>("forbidden", "The caller is not authorised to search this index.");

        ConsistencyDecision consistency = await _ports.Consistency.ResolveAsync(context, request.Consistency, cancellationToken).ConfigureAwait(false);
        if (!consistency.IsAllowed)
            return Failure<SearchResponse>("consistency_unavailable", consistency.Reason ?? "The requested consistency is unavailable.");

        if (!_registry.TryGet(indexName, out IndexRuntimeEntry? entry))
            return Failure<SearchResponse>("index_not_found", "The index does not exist.");

        if (request.Size is < 1 or > 1_000 || request.SearchAfter is not null || request.Sort is not null || request.Facets is not null)
            return Failure<SearchResponse>("unsupported_search", "The requested pagination, sort, or facet options are not available.");

        using SearcherLease lease = entry!.Runtime.Searchers.AcquireLease();
        TopDocs documents;
        switch (request.Query)
        {
            case QueryStringDefinition queryString:
                documents = lease.Searcher.Search(queryString.Text, queryString.DefaultField ?? entry.Registration.Settings.DefaultField ?? "content", request.Size);
                break;
            case TermQueryDefinition term:
                documents = lease.Searcher.Search(new TermQuery(term.Field, term.Value), request.Size);
                break;
            case PhraseQueryDefinition phrase:
                documents = lease.Searcher.Search(new PhraseQuery(phrase.Field, phrase.Slop, phrase.Terms.ToArray()), request.Size);
                break;
            case PrefixQueryDefinition prefix:
                documents = lease.Searcher.Search(new PrefixQuery(prefix.Field, prefix.Prefix), request.Size);
                break;
            case WildcardQueryDefinition wildcard:
                documents = lease.Searcher.Search(new WildcardQuery(wildcard.Field, wildcard.Pattern), request.Size);
                break;
            case RegexpQueryDefinition regexp:
                documents = lease.Searcher.Search(new RegexpQuery(regexp.Field, regexp.Pattern), request.Size);
                break;
            case BooleanQueryDefinition boolean:
                documents = lease.Searcher.Search(BuildBooleanQuery(boolean), request.Size);
                break;
            default:
                return Failure<SearchResponse>("unsupported_query", "The query type is not available on the local server.");
        }
        SearchHit[] hits = documents.ScoreDocs.Select(scoreDocument => ToSearchHit(lease, scoreDocument, request.IncludeDocuments)).ToArray();
        SearchResponse response = new(hits, documents.TotalHits, TotalHitsRelation.Exact, ScoringModel.ShardLocal, new ShardSearchSummary(1, 1, 0, 0), new SearchTiming(0), IsPartial: documents.IsPartial);
        await _ports.Audit.PublishAsync(new AuditEvent(context, true), cancellationToken).ConfigureAwait(false);
        return Success(response);
    }

    /// <inheritdoc />
    public ValueTask<ServiceResult<ExplainResponse>> ExplainAsync(string indexName, ExplainRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Failure<ExplainResponse>("not_implemented", "Score explanation is not available yet."));

    /// <inheritdoc />
    public ValueTask<ServiceResult<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Success(new HealthResponse(true, "healthy", DateTimeOffset.UtcNow)));

    /// <inheritdoc />
    public ValueTask<ServiceResult<ReadinessResponse>> GetReadinessAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(Success(new ReadinessResponse(true, "ready", DateTimeOffset.UtcNow)));

    /// <summary>Disposes all local index resources.</summary>
    public void Dispose() => _registry.Dispose();

    private ResponseMetadata Metadata() => new(Guid.NewGuid().ToString("N"), ServerApiVersions.V1, DateTimeOffset.UtcNow);

    private static OperationContext Context(OperationKind operation, string? indexName) =>
        new(Guid.NewGuid().ToString("N"), operation, CallerIdentity.Anonymous, DateTimeOffset.UtcNow, indexName);

    private ServiceResult<T> Success<T>(T value) => ServiceResult<T>.Success(Metadata(), value);

    private ServiceResult<T> Failure<T>(string code, string message) => ServiceResult<T>.Failed(Metadata(), new ApiFailure(code, message));

    private static IndexSummary ToSummary(IndexRuntimeEntry entry) => new(entry.Registration.Name, entry.Registration.Id, entry.Registration.SchemaHash, 0, entry.Registration.CreatedUtc);

    private static SearchHit ToSearchHit(SearcherLease lease, ScoreDoc scoreDocument, bool includeDocument)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> stored = lease.Searcher.GetStoredFields(scoreDocument.DocId);
        string documentId = stored.TryGetValue(ServerDocumentMapper.DocumentIdField, out IReadOnlyList<string>? identifiers) ? identifiers[0] : scoreDocument.DocId.ToString();
        JsonElement? document = null;
        if (includeDocument && stored.TryGetValue(ServerDocumentMapper.RawDocumentField, out IReadOnlyList<string>? rawDocuments))
            using (JsonDocument parsed = JsonDocument.Parse(rawDocuments[0]))
                document = parsed.RootElement.Clone();

        return new SearchHit(documentId, scoreDocument.Score, document);
    }

    private static BooleanQuery BuildBooleanQuery(BooleanQueryDefinition definition)
    {
        BooleanQuery.Builder builder = new();
        AddClauses(builder, definition.Must, Occur.Must);
        AddClauses(builder, definition.Should, Occur.Should);
        AddClauses(builder, definition.MustNot, Occur.MustNot);
        if (definition.MinimumShouldMatch is int minimum)
            builder.SetMinimumNumberShouldMatch(minimum);
        return builder.Build();
    }

    private static void AddClauses(BooleanQuery.Builder builder, IReadOnlyList<QueryDefinition>? definitions, Occur occur)
    {
        if (definitions is null)
            return;

        foreach (QueryDefinition definition in definitions)
            builder.Add(BuildQuery(definition), occur);
    }

    private static Query BuildQuery(QueryDefinition definition) => definition switch
    {
        TermQueryDefinition term => new TermQuery(term.Field, term.Value),
        PhraseQueryDefinition phrase => new PhraseQuery(phrase.Field, phrase.Slop, phrase.Terms.ToArray()),
        PrefixQueryDefinition prefix => new PrefixQuery(prefix.Field, prefix.Prefix),
        WildcardQueryDefinition wildcard => new WildcardQuery(wildcard.Field, wildcard.Pattern),
        RegexpQueryDefinition regexp => new RegexpQuery(regexp.Field, regexp.Pattern),
        BooleanQueryDefinition boolean => BuildBooleanQuery(boolean),
        _ => throw new NotSupportedException($"The query type '{definition.GetType().Name}' cannot be used in a boolean query.")
    };

    private static long DirectorySize(string path) => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length);
}
