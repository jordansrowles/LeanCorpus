using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Document;
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
using Rowles.LeanCorpus.Server.Core.Execution;
using Rowles.LeanCorpus.Server.Core.QueryTranslation;
using Rowles.LeanCorpus.Server.Core.Runtime;
using Rowles.LeanCorpus.Server.Core.Serialisation;
using Rowles.LeanCorpus.Server.Core.Storage;
using Rowles.LeanCorpus.Server.Abstractions.Serialisation;
using Rowles.LeanCorpus.Diagnostics;
namespace Rowles.LeanCorpus.Server.Core.Services;

/// <summary>Implements the transport-neutral Community Server operations.</summary>
public sealed class LocalServerCore : IIndexService, IHealthService, IDocumentService, ISearchService, IInspectionService, IDisposable
{
    private static readonly ActivitySource ActivitySource = new("Rowles.LeanCorpus.Server");
    private static readonly Meter ServerMeter = new("Rowles.LeanCorpus.Server", "0.1.0-alpha.1");
    private static readonly Counter<long> RequestCounter = ServerMeter.CreateCounter<long>("leancorpus.server.requests");
    private static readonly Histogram<double> RequestDuration = ServerMeter.CreateHistogram<double>("leancorpus.server.request.duration", "ms");
    private static readonly Counter<long> BulkOperationCounter = ServerMeter.CreateCounter<long>("leancorpus.server.bulk.operations");
    private static readonly Counter<long> BulkFailureCounter = ServerMeter.CreateCounter<long>("leancorpus.server.bulk.failures");
    private static readonly Histogram<double> SearchDuration = ServerMeter.CreateHistogram<double>("leancorpus.server.search.duration", "ms");
    private readonly LocalIndexRegistry _registry;
    private readonly ServerCoreOptions _options;
    private readonly ServerPortSet _ports;
    private readonly ILocalIndexExecutor _executor;
    private readonly DateTimeOffset _startedUtc;
    private readonly ConcurrentDictionary<string, IdempotencyStore> _idempotency = new(StringComparer.Ordinal);
    private readonly object _operationLock = new();
    private readonly ManualResetEventSlim _operationsDrained = new(initialState: true);
    private int _activeOperations;
    private int _draining;
    private int _disposed;

    private LocalServerCore(LocalIndexRegistry registry, ServerCoreOptions options, ServerPortSet ports, ILocalIndexExecutor executor)
    {
        _registry = registry;
        _options = options;
        _ports = ports;
        _executor = executor;
        _startedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Opens the local registry at the configured data root.</summary>
    public static async ValueTask<LocalServerCore> OpenAsync(
        ServerCoreOptions options,
        ServerPortSet? ports = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        LocalIndexRegistry registry = await LocalIndexRegistry
            .OpenAsync(Path.GetFullPath(options.DataRoot), options, cancellationToken)
            .ConfigureAwait(false);
        return new LocalServerCore(registry, options, ports ?? ServerPortSet.Community, new LocalIndexExecutor(options));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<IReadOnlyList<IndexSummary>>> ListAsync(CancellationToken cancellationToken = default)
    {
        using OperationStart start = await BeginAsync(OperationKind.ListIndexes, null, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<IReadOnlyList<IndexSummary>>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Public, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<IReadOnlyList<IndexSummary>>(start.Context, failure);
        IReadOnlyList<IndexSummary> summaries = _registry.List().Select(ToSummary).ToArray();
        return Success(start.Context, summaries);
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<IndexSummary>> CreateAsync(CreateIndexRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.CreateIndex, request.IndexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<IndexSummary>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Administrative, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<IndexSummary>(start.Context, failure);
        if (await RequireLocalFeatureAsync(start.Context, cancellationToken).ConfigureAwait(false) is { } featureFailure)
            return Failure<IndexSummary>(start.Context, featureFailure);
        if (!IndexName.IsValid(request.IndexName))
            return Failure<IndexSummary>(start.Context, new ApiFailure("invalid_index_name", "Index names may contain only ASCII letters, digits, underscores, and hyphens."));

        try
        {
            IndexSchemaValidator.Validate(request.Schema, request.Topology, request.Settings);
            CommunityTopologyValidator.Validate(request.Topology);
        }
        catch (InvalidOperationException exception)
        {
            return Failure<IndexSummary>(start.Context, new ApiFailure("invalid_topology", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Failure<IndexSummary>(start.Context, new ApiFailure("invalid_schema", exception.Message));
        }

        IndexRegistration registration = new(
            request.IndexName,
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            request.Schema,
            request.Topology,
            request.Settings,
            SchemaHash.Compute(request.Schema, request.Topology));

        await _ports.Lifecycle.OnTransitionAsync(
            new IndexLifecycleEvent(start.Context, request.IndexName, IndexLifecycleStage.Creating), cancellationToken).ConfigureAwait(false);
        try
        {
            IndexRuntimeEntry? entry = await _registry.CreateAsync(registration, cancellationToken).ConfigureAwait(false);
            if (entry is null)
            {
                await PublishAuditAsync(start.Context, false, "index_exists", cancellationToken).ConfigureAwait(false);
                return Failure<IndexSummary>(start.Context, new ApiFailure("index_exists", "An index with that name already exists."));
            }

            await _ports.Lifecycle.OnTransitionAsync(
                new IndexLifecycleEvent(start.Context, request.IndexName, IndexLifecycleStage.Created), cancellationToken).ConfigureAwait(false);
            await PublishAuditAsync(start.Context, true, null, cancellationToken).ConfigureAwait(false);
            return Success(start.Context, ToSummary(entry));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await PublishAuditAsync(start.Context, false, "storage_error", cancellationToken).ConfigureAwait(false);
            return Failure<IndexSummary>(start.Context, new ApiFailure("storage_error", "The local index storage operation failed."));
        }
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<bool>> DeleteAsync(DeleteIndexRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.DeleteIndex, request.IndexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<bool>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Administrative, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<bool>(start.Context, failure);
        if (await RequireLocalFeatureAsync(start.Context, cancellationToken).ConfigureAwait(false) is { } featureFailure)
            return Failure<bool>(start.Context, featureFailure);
        if (!IndexName.IsValid(request.IndexName))
            return Failure<bool>(start.Context, new ApiFailure("invalid_index_name", "The index name is invalid."));
        if (!ConfirmationTokens.IsValid(request.ConfirmationToken, "delete-index", request.IndexName))
            return Failure<bool>(start.Context, new ApiFailure("confirmation_required", "Deleting an index requires a valid confirmation token."));

        await _ports.Lifecycle.OnTransitionAsync(
            new IndexLifecycleEvent(start.Context, request.IndexName, IndexLifecycleStage.Deleting), cancellationToken).ConfigureAwait(false);
        try
        {
            bool deleted = await _registry.DeleteAsync(request.IndexName, cancellationToken).ConfigureAwait(false);
            if (!deleted)
            {
                await PublishAuditAsync(start.Context, false, "index_not_found", cancellationToken).ConfigureAwait(false);
                return Failure<bool>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));
            }

            await _ports.Lifecycle.OnTransitionAsync(
                new IndexLifecycleEvent(start.Context, request.IndexName, IndexLifecycleStage.Deleted), cancellationToken).ConfigureAwait(false);
            await PublishAuditAsync(start.Context, true, null, cancellationToken).ConfigureAwait(false);
            return Success(start.Context, true);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await PublishAuditAsync(start.Context, false, "storage_error", cancellationToken).ConfigureAwait(false);
            return Failure<bool>(start.Context, new ApiFailure("storage_error", "The local index storage operation failed."));
        }
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<IndexSchemaResponse>> GetSchemaAsync(string indexName, CancellationToken cancellationToken = default)
    {
        using OperationStart start = await BeginAsync(OperationKind.ReadIndexMetadata, indexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<IndexSchemaResponse>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Public, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<IndexSchemaResponse>(start.Context, failure);
        if (!TryGetEntry(indexName, out IndexRuntimeEntry? entry))
            return Failure<IndexSchemaResponse>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));
        return Success(start.Context, new IndexSchemaResponse(entry!.Registration.Name, entry.Registration.Schema, entry.Registration.SchemaHash, entry.Registration.Settings));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<IndexStatisticsResponse>> GetStatisticsAsync(string indexName, CancellationToken cancellationToken = default)
    {
        using OperationStart start = await BeginAsync(OperationKind.ReadIndexMetadata, indexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<IndexStatisticsResponse>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Public, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<IndexStatisticsResponse>(start.Context, failure);
        if (!TryGetEntry(indexName, out IndexRuntimeEntry? entry))
            return Failure<IndexStatisticsResponse>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));

        using SearcherLease lease = entry!.Runtime.Searchers.AcquireLease();
        var stats = lease.Searcher.Stats;
        var size = lease.Searcher.GetIndexSize();
        IndexStatisticsResponse response = new(
            indexName,
            entry.Registration.SchemaHash,
            stats.LiveDocCount,
            Math.Max(0, stats.TotalDocCount - stats.LiveDocCount),
            size.TotalSizeBytes,
            size.SegmentCount,
            lease.CommitGeneration);
        return Success(start.Context, response);
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<IndexSummary>> UpdateSettingsAsync(UpdateIndexSettingsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.UpdateIndexSettings, request.IndexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<IndexSummary>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Administrative, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<IndexSummary>(start.Context, failure);
        if (await RequireLocalFeatureAsync(start.Context, cancellationToken).ConfigureAwait(false) is { } featureFailure)
            return Failure<IndexSummary>(start.Context, featureFailure);
        if (!IndexName.IsValid(request.IndexName))
            return Failure<IndexSummary>(start.Context, new ApiFailure("invalid_index_name", "The index name is invalid."));
        if (!ConfirmationTokens.IsValid(request.ConfirmationToken ?? string.Empty, "update-settings", request.IndexName))
            return Failure<IndexSummary>(start.Context, new ApiFailure("confirmation_required", "Changing index settings requires a valid confirmation token."));
        if (!TryGetEntry(request.IndexName, out IndexRuntimeEntry? existing))
            return Failure<IndexSummary>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));

        try
        {
            IndexSchemaValidator.Validate(existing!.Registration.Schema, existing.Registration.Topology, request.Settings);
            IndexRuntimeEntry? entry = await _registry.UpdateSettingsAsync(request.IndexName, request.Settings, cancellationToken).ConfigureAwait(false);
            if (entry is null)
                return Failure<IndexSummary>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));
            await PublishAuditAsync(start.Context, true, null, cancellationToken).ConfigureAwait(false);
            return Success(start.Context, ToSummary(entry));
        }
        catch (ArgumentException exception)
        {
            return Failure<IndexSummary>(start.Context, new ApiFailure("invalid_settings", exception.Message));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            await PublishAuditAsync(start.Context, false, "storage_error", cancellationToken).ConfigureAwait(false);
            return Failure<IndexSummary>(start.Context, new ApiFailure("storage_error", "The local index storage operation failed."));
        }
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<RefreshIndexResponse>> RefreshAsync(RefreshIndexRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.RefreshIndex, request.IndexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<RefreshIndexResponse>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Public, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<RefreshIndexResponse>(start.Context, failure);
        if (await RequireLocalFeatureAsync(start.Context, cancellationToken).ConfigureAwait(false) is { } featureFailure)
            return Failure<RefreshIndexResponse>(start.Context, featureFailure);
        if (!TryGetEntry(request.IndexName, out IndexRuntimeEntry? entry))
            return Failure<RefreshIndexResponse>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));

        entry!.Runtime.Commit(refresh: true);
        using SearcherLease lease = entry.Runtime.Searchers.AcquireLease();
        await PublishAuditAsync(start.Context, true, null, cancellationToken).ConfigureAwait(false);
        return Success(start.Context, new RefreshIndexResponse(request.IndexName, lease.CommitGeneration));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<BulkDocumentsResponse>> BulkAsync(BulkDocumentsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.WriteDocuments, request.IndexName, null, request.IdempotencyKey, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<BulkDocumentsResponse>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Public, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<BulkDocumentsResponse>(start.Context, failure);

        OperationRoute route = await _ports.Router.RouteAsync(new OperationRouteRequest(start.Context, true), cancellationToken).ConfigureAwait(false);
        if (route is RejectedRoute or RemoteRoute)
            return Failure<BulkDocumentsResponse>(start.Context, new ApiFailure("route_unavailable", "The write cannot execute on this server."));
        if (await RequireLocalFeatureAsync(start.Context, cancellationToken).ConfigureAwait(false) is { } featureFailure)
            return Failure<BulkDocumentsResponse>(start.Context, featureFailure);
        if (!TryGetEntry(request.IndexName, out IndexRuntimeEntry? entry))
            return Failure<BulkDocumentsResponse>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));
        if (request.Operations is null || request.Operations.Count < 1 || request.Operations.Count > _options.MaximumBulkOperations)
            return Failure<BulkDocumentsResponse>(start.Context, new ApiFailure("invalid_bulk_request", "The request contains an invalid number of document operations."));
        if (!Enum.IsDefined(request.Durability))
            return Failure<BulkDocumentsResponse>(start.Context, new ApiFailure("invalid_durability", "The requested write durability is not recognised."));
        if (request.Durability is RequestedWriteDurability.Quorum or RequestedWriteDurability.Replicated)
            return Failure<BulkDocumentsResponse>(start.Context, new ApiFailure("durability_not_supported", "Community Server supports Memory and LocalFsync write durability only."));

        IdempotencyStore? idempotency = null;
        string? fingerprint = null;
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            idempotency = _idempotency.GetOrAdd(entry!.Registration.Id, _ => new IdempotencyStore(_options.MaximumIdempotencyEntries));
            fingerprint = Fingerprint(request);
            string scopedKey = start.Context.Caller.SubjectId + ":" + request.IdempotencyKey;
            if (idempotency.TryGet(scopedKey, fingerprint, out BulkDocumentsResponse? replay, out bool conflict))
            {
                if (conflict)
                    return Failure<BulkDocumentsResponse>(start.Context, new ApiFailure("idempotency_conflict", "The idempotency key was already used for a different request."));
                return Success(start.Context, replay!);
            }
        }

        BulkOperationCounter.Add(request.Operations.Count);
        LocalWriteResult local = await _executor.WriteAsync(start.Context, entry!.Handle, request, cancellationToken).ConfigureAwait(false);
        foreach (BulkDocumentResult item in local.Items)
            if (!item.Accepted)
                BulkFailureCounter.Add(1);
        WriteAcknowledgement acknowledgement = await _ports.WriteAcknowledgements.AcknowledgeAsync(
            new WriteCommitState(start.Context, request.IndexName, local.SequenceNumber, local.Committed, request.Refresh && local.Committed && local.AcceptedOperations > 0), cancellationToken).ConfigureAwait(false);
        WriteToken? token = local.AcceptedOperations == 0
            ? null
            : new WriteToken(1, entry.Registration.Id, local.SequenceNumber, local.Receipt?.CommitGeneration, local.Receipt?.ContentToken);
        BulkDocumentsResponse response = new(local.Items, acknowledgement.IsAcknowledged, local.Receipt?.CommitGeneration ?? local.VisibleGeneration, token);
        if (idempotency is not null && fingerprint is not null)
            idempotency.Add(start.Context.Caller.SubjectId + ":" + request.IdempotencyKey, fingerprint, response);
        await PublishAuditAsync(start.Context, acknowledgement.IsAcknowledged, acknowledgement.IsAcknowledged ? null : "write_not_acknowledged", cancellationToken).ConfigureAwait(false);
        return Success(start.Context, response);
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<SearchResponse>> SearchAsync(string indexName, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.Search, indexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<SearchResponse>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Public, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<SearchResponse>(start.Context, failure);
        OperationRoute route = await _ports.Router.RouteAsync(new OperationRouteRequest(start.Context, false), cancellationToken).ConfigureAwait(false);
        if (route is RejectedRoute or RemoteRoute)
            return Failure<SearchResponse>(start.Context, new ApiFailure("route_unavailable", "The search cannot execute on this server."));
        if (await RequireLocalFeatureAsync(start.Context, cancellationToken).ConfigureAwait(false) is { } featureFailure)
            return Failure<SearchResponse>(start.Context, featureFailure);
        ConsistencyDecision consistency = await _ports.Consistency.ResolveAsync(start.Context, request.Consistency, cancellationToken).ConfigureAwait(false);
        if (!consistency.IsAllowed)
            return Failure<SearchResponse>(start.Context, new ApiFailure("consistency_unavailable", consistency.Reason ?? "The requested consistency is unavailable."));
        if (!TryGetEntry(indexName, out IndexRuntimeEntry? entry))
            return Failure<SearchResponse>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));
        if (request.Consistency == RequestedConsistency.ReadYourWrites)
        {
            if (request.ReadToken is not { } token)
                return Failure<SearchResponse>(start.Context, new ApiFailure("write_token_required", "ReadYourWrites consistency requires a write token."));
            if (token.Version != 1 || token.SequenceNumber <= 0 || !string.Equals(token.IndexId, entry!.Registration.Id, StringComparison.Ordinal))
                return Failure<SearchResponse>(start.Context, new ApiFailure("invalid_write_token", "The write token does not belong to this index."));
            try
            {
                using CancellationTokenSource consistencyWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                consistencyWait.CancelAfter(_options.MaximumConsistencyWait);
                await entry.Runtime.Commits.WaitUntilCommittedAsync(token.SequenceNumber, consistencyWait.Token).ConfigureAwait(false);
                entry.Runtime.Refresh();
            }
            catch (OperationCanceledException)
            {
                return Failure<SearchResponse>(start.Context, new ApiFailure(cancellationToken.IsCancellationRequested ? "consistency_wait_cancelled" : "consistency_wait_timeout", "Waiting for the requested write did not complete within the configured limit.", true));
            }
        }
        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            SearchResponse response = await _executor.SearchAsync(start.Context, entry!.Handle, request, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            RequestDuration.Record(stopwatch.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("operation", nameof(OperationKind.Search)));
            SearchDuration.Record(stopwatch.Elapsed.TotalMilliseconds);
            await PublishAuditAsync(start.Context, true, null, cancellationToken).ConfigureAwait(false);
            return Success(start.Context, response);
        }
        catch (LocalExecutionException exception)
        {
            return Failure<SearchResponse>(start.Context, exception.Failure);
        }
        catch (NotSupportedException exception)
        {
            return Failure<SearchResponse>(start.Context, new ApiFailure("unsupported_search", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Failure<SearchResponse>(start.Context, new ApiFailure("invalid_sort", exception.Message));
        }
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<ExplainResponse>> ExplainAsync(string indexName, ExplainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.Search, indexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<ExplainResponse>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Public, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<ExplainResponse>(start.Context, failure);
        OperationRoute route = await _ports.Router.RouteAsync(new OperationRouteRequest(start.Context, false), cancellationToken).ConfigureAwait(false);
        if (route is RejectedRoute or RemoteRoute)
            return Failure<ExplainResponse>(start.Context, new ApiFailure("route_unavailable", "The explanation cannot execute on this server."));
        if (await RequireLocalFeatureAsync(start.Context, cancellationToken).ConfigureAwait(false) is { } featureFailure)
            return Failure<ExplainResponse>(start.Context, featureFailure);
        if (!TryGetEntry(indexName, out IndexRuntimeEntry? entry))
            return Failure<ExplainResponse>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));
        try
        {
            ExplainResponse response = await _executor.ExplainAsync(start.Context, entry!.Handle, request, cancellationToken).ConfigureAwait(false);
            await PublishAuditAsync(start.Context, true, null, cancellationToken).ConfigureAwait(false);
            return Success(start.Context, response);
        }
        catch (LocalExecutionException exception)
        {
            return Failure<ExplainResponse>(start.Context, exception.Failure);
        }
        catch (NotSupportedException exception)
        {
            return Failure<ExplainResponse>(start.Context, new ApiFailure("explain_not_supported", exception.Message));
        }
        catch (ArgumentException exception)
        {
            return Failure<ExplainResponse>(start.Context, new ApiFailure("invalid_explain_request", exception.Message));
        }
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<InspectionResponse>> InspectAsync(string indexName, InspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using OperationStart start = await BeginAsync(OperationKind.Inspect, indexName, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<InspectionResponse>(start.Context, authenticationFailure);
        if (await AuthoriseAsync(start.Context, EndpointAccess.Administrative, cancellationToken).ConfigureAwait(false) is { } failure)
            return Failure<InspectionResponse>(start.Context, failure);
        InspectionDecision decision = await _ports.Inspection.EvaluateAsync(start.Context, request, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
            return Failure<InspectionResponse>(start.Context, new ApiFailure("inspection_denied", decision.Reason ?? "The inspection request is not allowed."));
        if (request.Limit < 1 || request.Limit > _options.MaximumInspectionItems || request.Limit > decision.MaximumLimit)
            return Failure<InspectionResponse>(start.Context, new ApiFailure("invalid_inspection_limit", "The inspection limit is outside the configured bounds."));

        IndexRuntimeEntry? entry = null;
        if (request.Resource is not InspectionResource.IndexInventory)
        {
            if (!TryGetEntry(indexName, out entry))
                return Failure<InspectionResponse>(start.Context, new ApiFailure("index_not_found", "The index does not exist."));
        }

        object payload;
        bool truncated = false;
        switch (request.Resource)
        {
            case InspectionResource.IndexInventory:
                IReadOnlyList<IndexRuntimeEntry> inventory = _registry.List();
                payload = inventory.Take(request.Limit).Select(ToSummary).ToArray();
                truncated = inventory.Count > request.Limit;
                break;
            case InspectionResource.Storage:
                using (SearcherLease storageLease = entry!.Runtime.Searchers.AcquireLease())
                {
                    var size = storageLease.Searcher.GetIndexSize();
                    payload = new StorageInspectionPayload(indexName, size.TotalSizeBytes, size.SegmentCount, size.Segments);
                }
                break;
            case InspectionResource.ReaderState:
                ReaderManagerDiagnostics diagnostics = entry!.Runtime.Searchers.GetDiagnostics();
                payload = new ReaderInspectionPayload(
                    indexName,
                    diagnostics.ActiveReaders,
                    diagnostics.ActiveLeases,
                    diagnostics.Refreshes,
                    diagnostics.RefreshFailures,
                    diagnostics.DisposedReaders,
                    entry.Runtime.Searchers.ConsecutiveRefreshFailures,
                    entry.Runtime.Searchers.LastRefreshError?.Message);
                break;
            case InspectionResource.Fields:
                payload = entry!.Registration.Schema.Fields.ToArray();
                break;
            case InspectionResource.Segments:
                using (SearcherLease segmentLease = entry!.Runtime.Searchers.AcquireLease())
                    payload = segmentLease.Searcher.GetIndexSize().Segments.Take(request.Limit).ToArray();
                break;
            case InspectionResource.Documents:
                using (SearcherLease documentLease = entry!.Runtime.Searchers.AcquireLease())
                {
                    TopDocs docs = documentLease.Searcher.Search(new MatchAllDocsQuery(), request.Limit);
                    payload = docs.ScoreDocs.Select(score => ToBoundedInspectionDocument(documentLease, score, _options.MaximumInspectionValueLength)).ToArray();
                    truncated = docs.TotalHits > request.Limit;
                }
                break;
            case InspectionResource.Terms:
            case InspectionResource.Postings:
            case InspectionResource.Analysis:
            case InspectionResource.VectorGraph:
            case InspectionResource.EnterpriseTopology:
                return Failure<InspectionResponse>(start.Context, new ApiFailure("inspection_not_supported", $"Inspection resource '{request.Resource}' is not available in Community Server 0.1."));
            default:
                return Failure<InspectionResponse>(start.Context, new ApiFailure("invalid_inspection_resource", "The inspection resource is not recognised."));
        }

        JsonElement data = payload switch
        {
            IndexSummary[] inventory => JsonSerializer.SerializeToElement(inventory, ServerCoreJsonSerialiserContext.Default.IndexSummaryArray),
            StorageInspectionPayload storage => JsonSerializer.SerializeToElement(storage, ServerCoreJsonSerialiserContext.Default.StorageInspectionPayload),
            ReaderInspectionPayload reader => JsonSerializer.SerializeToElement(reader, ServerCoreJsonSerialiserContext.Default.ReaderInspectionPayload),
            IndexFieldDefinition[] fields => JsonSerializer.SerializeToElement(fields, ServerCoreJsonSerialiserContext.Default.IndexFieldDefinitionArray),
            SegmentSizeReport[] segments => JsonSerializer.SerializeToElement(segments, ServerCoreJsonSerialiserContext.Default.SegmentSizeReportArray),
            BoundedInspectionDocument[] documents => JsonSerializer.SerializeToElement(documents, ServerCoreJsonSerialiserContext.Default.BoundedInspectionDocumentArray),
            _ => throw new InvalidOperationException("The inspection payload type is not registered for serialisation.")
        };
        await PublishAuditAsync(start.Context, true, null, cancellationToken).ConfigureAwait(false);
        return Success(start.Context, new InspectionResponse(request.Resource, data, truncated));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<HealthResponse>> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        using OperationStart start = await BeginAsync(OperationKind.ReadHealth, null, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<HealthResponse>(start.Context, authenticationFailure);
        DateTimeOffset observedUtc = DateTimeOffset.UtcNow;
        if (Volatile.Read(ref _disposed) != 0)
            return Success(start.Context, new HealthResponse(false, "unhealthy", observedUtc, [], "server stopped."));
        if (Volatile.Read(ref _draining) != 0)
            return Success(start.Context, new HealthResponse(false, "draining", observedUtc, [], "server is draining."));

        (IReadOnlyList<IndexHealthSummary> indices, bool hasUnusable, bool hasDegraded) = ReadIndexHealth();
        string status = hasUnusable ? "unhealthy" : hasDegraded ? "degraded" : "healthy";
        string? reason = hasUnusable
            ? "One or more indexes are unusable."
            : hasDegraded
                ? "One or more indexes are degraded; previously committed reads remain available."
                : null;
        return Success(start.Context, new HealthResponse(status == "healthy", status, observedUtc, indices, reason));
    }

    /// <inheritdoc />
    public async ValueTask<ServiceResult<ReadinessResponse>> GetReadinessAsync(CancellationToken cancellationToken = default)
    {
        using OperationStart start = await BeginAsync(OperationKind.ReadReadiness, null, null, null, cancellationToken).ConfigureAwait(false);
        if (start.Failure is { } authenticationFailure)
            return Failure<ReadinessResponse>(start.Context, authenticationFailure);
        if (Volatile.Read(ref _disposed) != 0)
            return Success(start.Context, new ReadinessResponse(false, "unhealthy", DateTimeOffset.UtcNow, "server stopped."));
        if (Volatile.Read(ref _draining) != 0)
            return Success(start.Context, new ReadinessResponse(false, "draining", DateTimeOffset.UtcNow, "server is draining."));

        (IReadOnlyList<IndexHealthSummary> _, bool hasUnusable, bool hasDegraded) = ReadIndexHealth();
        if (hasUnusable)
            return Success(start.Context, new ReadinessResponse(false, "unhealthy", DateTimeOffset.UtcNow, "One or more indexes are unusable."));

        // Degraded indexes remain ready because previously committed reads are
        // still available. An unusable index is the readiness boundary.
        return Success(start.Context, new ReadinessResponse(true, "ready", DateTimeOffset.UtcNow,
            hasDegraded ? "One or more indexes are degraded; writes may be unavailable." : null));
    }

    /// <summary>Disposes all local index resources.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _draining, 1) != 0)
            return;

        _operationsDrained.Wait(_options.ShutdownTimeout);
        Interlocked.Exchange(ref _disposed, 1);
        _registry.Dispose();
    }

    private async ValueTask<OperationStart> BeginAsync(OperationKind operation, string? indexName, string? correlationId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N");
        RequestCounter.Add(1, new KeyValuePair<string, object?>("operation", operation.ToString()));
        bool observesLifecycleOnly = operation is OperationKind.ReadHealth or OperationKind.ReadReadiness;
        if (!observesLifecycleOnly && !TryEnterOperation())
        {
            OperationContext rejected = new(requestId, operation, CallerIdentity.Anonymous, DateTimeOffset.UtcNow, indexName, correlationId, idempotencyKey);
            return new OperationStart(this, rejected, new ApiFailure("server_stopping", "The server is stopping and is not accepting new operations."), entered: false);
        }

        AuthenticationResult authentication;
        try
        {
            authentication = await _ports.Authentication.AuthenticateAsync(new AuthenticationRequest(null, null), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (!observesLifecycleOnly)
                ExitOperation();
            throw;
        }
        CallerIdentity caller = authentication.Caller ?? CallerIdentity.Anonymous;
        OperationContext context = new(requestId, operation, caller, DateTimeOffset.UtcNow, indexName, correlationId, idempotencyKey);
        return authentication.FailureReason is { Length: > 0 } reason
            ? new OperationStart(this, context, new ApiFailure("unauthenticated", reason), !observesLifecycleOnly)
            : new OperationStart(this, context, null, !observesLifecycleOnly);
    }

    private bool TryEnterOperation()
    {
        lock (_operationLock)
        {
            if (_draining != 0)
                return false;
            if (_activeOperations++ == 0)
                _operationsDrained.Reset();
            return true;
        }
    }

    private void ExitOperation()
    {
        lock (_operationLock)
        {
            if (--_activeOperations == 0)
                _operationsDrained.Set();
        }
    }

    private async ValueTask<ApiFailure?> AuthoriseAsync(OperationContext context, EndpointAccess access, CancellationToken cancellationToken)
    {
        AuthorisationDecision decision = await _ports.Authorisation.AuthoriseAsync(new OperationPermission(context, access), cancellationToken).ConfigureAwait(false);
        return decision.IsAllowed ? null : new ApiFailure("forbidden", "The caller is not authorised to perform this operation.");
    }

    private async ValueTask<ApiFailure?> RequireLocalFeatureAsync(OperationContext context, CancellationToken cancellationToken)
    {
        EntitlementDecision entitlement = await _ports.Entitlements.EvaluateAsync(ServerFeature.LocalServer, cancellationToken).ConfigureAwait(false);
        return entitlement.IsAllowed ? null : new ApiFailure("feature_unavailable", entitlement.Reason ?? "The local server feature is unavailable.");
    }

    private async ValueTask PublishAuditAsync(OperationContext context, bool success, string? failureCode, CancellationToken cancellationToken) =>
        await _ports.Audit.PublishAsync(new AuditEvent(context, success, failureCode), cancellationToken).ConfigureAwait(false);

    private bool TryGetEntry(string indexName, out IndexRuntimeEntry? entry)
    {
        entry = null;
        return !string.IsNullOrWhiteSpace(indexName) && _registry.TryGet(indexName, out entry);
    }

    private ResponseMetadata Metadata(OperationContext context) => new(context.RequestId, ServerApiVersions.V1, DateTimeOffset.UtcNow);

    private ServiceResult<T> Success<T>(OperationContext context, T value) => ServiceResult<T>.Success(Metadata(context), value);

    private ServiceResult<T> Failure<T>(OperationContext context, ApiFailure failure) => ServiceResult<T>.Failed(Metadata(context), failure);

    private static IndexSummary ToSummary(IndexRuntimeEntry entry)
    {
        using SearcherLease lease = entry.Runtime.Searchers.AcquireLease();
        return new IndexSummary(entry.Registration.Name, entry.Registration.Id, entry.Registration.SchemaHash, lease.Searcher.Stats.LiveDocCount, entry.Registration.CreatedUtc);
    }

    private (IReadOnlyList<IndexHealthSummary> Indices, bool HasUnusable, bool HasDegraded) ReadIndexHealth()
    {
        IndexRuntimeEntry[] entries = _registry.List().ToArray();
        IndexHealthSummary[] indices = new IndexHealthSummary[entries.Length];
        bool hasUnusable = false;
        bool hasDegraded = false;
        for (int i = 0; i < entries.Length; i++)
        {
            IndexRuntimeEntry entry = entries[i];
            LocalIndexHealth health = entry.Handle.Health;
            indices[i] = new IndexHealthSummary(
                entry.Registration.Name,
                entry.Registration.Id,
                health.Mode.ToString(),
                health.VisibleGeneration,
                health.DurableGeneration,
                health.PendingOperations,
                health.LastSuccessfulCommitUtc,
                health.LastCommitError,
                health.ConsecutiveCommitFailures,
                health.ActiveSnapshotLeases,
                health.IsInstalling,
                health.IsUsable,
                health.IsDegraded,
                health.LastInstallError);
            hasUnusable |= !health.IsUsable;
            hasDegraded |= health.IsDegraded;
        }
        return (indices, hasUnusable, hasDegraded);
    }

    private static BoundedInspectionDocument ToBoundedInspectionDocument(SearcherLease lease, ScoreDoc scoreDocument, int maximumValueLength)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> stored = lease.Searcher.GetStoredFields(scoreDocument.DocId);
        string documentId = stored.TryGetValue(ServerDocumentMapper.DocumentIdField, out IReadOnlyList<string>? identifiers) && identifiers.Count > 0
            ? identifiers[0]
            : scoreDocument.DocId.ToString(CultureInfo.InvariantCulture);
        string raw = stored.TryGetValue(ServerDocumentMapper.RawDocumentField, out IReadOnlyList<string>? rawDocuments) && rawDocuments.Count > 0
            ? rawDocuments[0]
            : string.Empty;
        bool truncated = raw.Length > maximumValueLength;
        return new BoundedInspectionDocument(documentId, truncated ? raw[..maximumValueLength] : raw, truncated);
    }

    private static ExplainResponse ToExplainResponse(Explanation explanation) =>
        new(explanation.Score > 0f, explanation.Score, explanation.Description, explanation.Details.Select(ToExplainResponse).ToArray());

    private static string Fingerprint(BulkDocumentsRequest request)
    {
        string serialised = JsonSerializer.Serialize(request, ServerJsonSerialiserContext.Default.BulkDocumentsRequest);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialised)));
    }

    private sealed class OperationStart(LocalServerCore owner, OperationContext context, ApiFailure? failure, bool entered) : IDisposable
    {
        private int _disposed;

        internal OperationContext Context { get; } = context;

        internal ApiFailure? Failure { get; } = failure;

        public void Dispose()
        {
            if (entered && Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.ExitOperation();
        }
    }
}
