using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrchardCore.Indexing;
using OrchardCore.Indexing.Models;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.OrchardCore.Search.Storage;
using Rowles.LeanCorpus.OrchardCore.Search.Models;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.OrchardCore.Search.Indexing;

/// <summary>Maps Orchard document-index batches and commits them durably to LeanCorpus.</summary>
public sealed class LeanCorpusDocumentIndexManager : IDocumentIndexManager
{
    private readonly LeanCorpusIndexPathResolver _paths;
    private readonly LeanCorpusIndexHandleCache _handles;
    private readonly LeanCorpusSchemaStore _schemas;
    private readonly LeanCorpusDocumentMapper _mapper;
    private readonly ILeanCorpusFailureInjector _failures;
    private readonly LeanCorpusContentFieldRefresher? _contentFieldRefresher;
    private readonly ILogger<LeanCorpusDocumentIndexManager> _logger;

    public LeanCorpusDocumentIndexManager(IServiceProvider services)
        : this(
            services.GetRequiredService<LeanCorpusIndexPathResolver>(),
            services.GetRequiredService<LeanCorpusIndexHandleCache>(),
            services.GetRequiredService<LeanCorpusSchemaStore>(),
            services.GetRequiredService<LeanCorpusDocumentMapper>(),
            services.GetRequiredService<ILeanCorpusFailureInjector>(),
            services.GetRequiredService<LeanCorpusContentFieldRefresher>(),
            services.GetRequiredService<ILogger<LeanCorpusDocumentIndexManager>>())
    {
    }

    internal LeanCorpusDocumentIndexManager(
        LeanCorpusIndexPathResolver paths,
        LeanCorpusIndexHandleCache handles,
        LeanCorpusSchemaStore schemas,
        LeanCorpusDocumentMapper mapper,
        ILeanCorpusFailureInjector failures,
        ILogger<LeanCorpusDocumentIndexManager> logger)
        : this(paths, handles, schemas, mapper, failures, contentFieldRefresher: null, logger)
    {
    }

    internal LeanCorpusDocumentIndexManager(
        LeanCorpusIndexPathResolver paths,
        LeanCorpusIndexHandleCache handles,
        LeanCorpusSchemaStore schemas,
        LeanCorpusDocumentMapper mapper,
        ILeanCorpusFailureInjector failures,
        LeanCorpusContentFieldRefresher? contentFieldRefresher,
        ILogger<LeanCorpusDocumentIndexManager> logger)
    {
        _paths = paths;
        _handles = handles;
        _schemas = schemas;
        _mapper = mapper;
        _failures = failures;
        _contentFieldRefresher = contentFieldRefresher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> AddOrUpdateDocumentsAsync(IndexProfile indexProfile, IEnumerable<DocumentIndex> documents)
    {
        DocumentIndex[] batch = documents?.ToArray() ?? [];
        if (batch.Length == 0)
            return false;

        LeanCorpusIndexPaths paths = _paths.Resolve(indexProfile.IndexFullName);
        return await _handles.WithExclusiveAsync(paths, async entry =>
        {
            try
            {
                DocumentIndex[] batchToMap = _contentFieldRefresher is null
                    ? batch
                    : await _contentFieldRefresher.RefreshAsync(batch, GetContentIndexSettings()).ConfigureAwait(false);
                batchToMap = batchToMap
                    .GroupBy(document => document.Id, StringComparer.Ordinal)
                    .Select(group => group.Last())
                    .ToArray();
                _ = await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(paths.StatePath).ConfigureAwait(false);
                LeanCorpusSchemaManifest current = await _schemas.ReadAsync(paths.SchemaPath).ConfigureAwait(false);
                LeanCorpusIndexMetadata metadata = indexProfile.GetLeanCorpusMetadata(current.Metadata);
                if (current.Fields.Count > 0 && current.Metadata.DefaultAnalyser != metadata.DefaultAnalyser)
                    throw new InvalidDataException("Changing the default analyser requires a physical index rebuild.");
                if (!LeanCorpusIndexProfileMetadataExtensions.MetadataEquals(current.Metadata, metadata))
                {
                    bool analyserChanged = current.Metadata.DefaultAnalyser != metadata.DefaultAnalyser;
                    current = current with { Metadata = metadata };
                    await _schemas.PublishAsync(paths.SchemaPath, current).ConfigureAwait(false);
                    if (analyserChanged)
                        await entry.CloseAsync().ConfigureAwait(false);
                }
                LeanCorpusMappedBatch mapped = _mapper.Map(batchToMap, current);
                if (!SameSchema(current, mapped.Manifest))
                    await _schemas.PublishAsync(paths.SchemaPath, mapped.Manifest).ConfigureAwait(false);

                _failures.Check(LeanCorpusFailurePoint.BeforeDocumentWrite);
                LeanCorpusIndexHandle handle = entry.Open(createIfMissing: false, current);
                using var runtimeOperation = handle.EnterOperation();
                var updates = new (string Term, LeanDocument Replacement)[batchToMap.Length];
                for (int index = 0; index < batchToMap.Length; index++)
                    updates[index] = (batchToMap[index].Id, mapped.Documents[index]);
                handle.Writer.UpdateDocuments(LeanCorpusDocumentMapper.DocumentIdField, updates);

                _failures.Check(LeanCorpusFailurePoint.AfterDocumentWriteBeforeCommit);
                handle.CommitAndRefresh(runtimeOperation);
                _failures.Check(LeanCorpusFailurePoint.AfterCommitBeforeCursor);
                return true;
            }
            catch (InvalidDataException exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus cursor or schema state is invalid for index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
            catch (Exception exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus document batch failed for index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteDocumentsAsync(IndexProfile indexProfile, IEnumerable<string> documentIds)
    {
        string[] ids = documentIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (ids.Length == 0)
            return false;

        LeanCorpusIndexPaths paths = _paths.Resolve(indexProfile.IndexFullName);
        return await _handles.WithExclusiveAsync(paths, async entry =>
        {
            try
            {
                _ = await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(paths.StatePath).ConfigureAwait(false);
                LeanCorpusIndexHandle handle = entry.Open(createIfMissing: false);
                using var runtimeOperation = handle.EnterOperation();
                foreach (string id in ids)
                    handle.Writer.DeleteDocuments(new TermQuery(LeanCorpusDocumentMapper.DocumentIdField, id));
                handle.CommitAndRefresh(runtimeOperation);
                _failures.Check(LeanCorpusFailurePoint.AfterDeleteCommitBeforeCursor);
                return true;
            }
            catch (InvalidDataException exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus cursor state is invalid for index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
            catch (Exception exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus document delete failed for index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAllDocumentsAsync(IndexProfile indexProfile)
    {
        LeanCorpusIndexPaths paths = _paths.Resolve(indexProfile.IndexFullName);
        return await _handles.WithExclusiveAsync(paths, async entry =>
        {
            try
            {
                _ = await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(paths.StatePath).ConfigureAwait(false);
                LeanCorpusIndexHandle handle = entry.Open(createIfMissing: false);
                using var runtimeOperation = handle.EnterOperation();
                DeleteAllDocuments(handle, runtimeOperation);
                return true;
            }
            catch (InvalidDataException exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus cursor state is invalid for index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
            catch (Exception exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus delete-all failed for index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
        }).ConfigureAwait(false);
    }

    internal async Task<bool> ResetAsync(IndexProfile indexProfile)
    {
        LeanCorpusIndexPaths paths = _paths.Resolve(indexProfile.IndexFullName);
        return await _handles.WithExclusiveAsync(paths, async entry =>
        {
            try
            {
                _ = await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(paths.StatePath).ConfigureAwait(false);
                LeanCorpusIndexHandle handle = entry.Open(createIfMissing: false);
                using var runtimeOperation = handle.EnterOperation();

                // Rewind before deleting. A crash from this point onwards makes Orchard replay tasks
                // from zero, so the old documents can be safely replaced or deleted again.
                await LeanCorpusIndexingStateStore.PublishAsync(paths.StatePath, 0, _failures).ConfigureAwait(false);
                DeleteAllDocuments(handle, runtimeOperation);
                return true;
            }
            catch (InvalidDataException exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus cursor state is invalid for reset of index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
            catch (Exception exception)
            {
                await entry.CloseAsync().ConfigureAwait(false);
                _logger.LogError(exception, "LeanCorpus reset failed for index {IndexFullName}.", indexProfile.IndexFullName);
                return false;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<long> GetLastTaskIdAsync(IndexProfile indexProfile)
    {
        LeanCorpusIndexPaths paths = _paths.Resolve(indexProfile.IndexFullName);
        return await _handles.WithExclusiveAsync(paths, async entry =>
        {
            try
            {
                return await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(paths.StatePath).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "LeanCorpus cursor state is invalid for index {IndexFullName}.", indexProfile.IndexFullName);
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetLastTaskIdAsync(IndexProfile indexProfile, long lastTaskId)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastTaskId);
        LeanCorpusIndexPaths paths = _paths.Resolve(indexProfile.IndexFullName);
        await _handles.WithExclusiveAsync(paths, async entry =>
        {
            try
            {
                _ = await LeanCorpusIndexingStateStore.ReadLastTaskIdAsync(paths.StatePath).ConfigureAwait(false);
                await LeanCorpusIndexingStateStore.PublishAsync(paths.StatePath, lastTaskId, _failures).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to publish LeanCorpus cursor for index {IndexFullName}.", indexProfile.IndexFullName);
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IContentIndexSettings GetContentIndexSettings() => new LeanCorpusContentIndexSettings();

    private static bool SameSchema(LeanCorpusSchemaManifest left, LeanCorpusSchemaManifest right)
        => left.Fields.Count == right.Fields.Count
            && left.Fields.All(pair => right.Fields.TryGetValue(pair.Key, out var value) && value == pair.Value);

    private static void DeleteAllDocuments(LeanCorpusIndexHandle handle, Rowles.LeanCorpus.Index.PhysicalIndexRuntime.OperationLease runtimeOperation)
    {
        using (var lease = handle.AcquireSearcher())
        {
            var query = new MatchAllDocsQuery();
            TopDocs hits = lease.Searcher.Search(query, 256);
            while (true)
            {
                foreach (var hit in hits.ScoreDocs)
                {
                    var fields = lease.Searcher.GetStoredFields(hit.DocId);
                    if (fields.TryGetValue(LeanCorpusDocumentMapper.DocumentIdField, out var values))
                    {
                        foreach (string id in values)
                            handle.Writer.DeleteDocuments(new TermQuery(LeanCorpusDocumentMapper.DocumentIdField, id));
                    }
                }

                if (hits.ScoreDocs.Length < 256)
                    break;

                hits = lease.Searcher.SearchAfter(hits.ScoreDocs[^1], query, 256, SortField.Score);
            }
        }

        handle.CommitAndRefresh(runtimeOperation);
    }
}
