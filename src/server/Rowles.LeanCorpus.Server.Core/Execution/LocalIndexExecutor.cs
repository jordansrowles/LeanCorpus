using System.Diagnostics;
using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.QueryTranslation;
using Rowles.LeanCorpus.Server.Core.Runtime;
using ServerFacetBucket = Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.FacetBucket;
using ServerFacetResult = Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.FacetResult;
using EngineFacetResult = Rowles.LeanCorpus.Search.Scoring.FacetResult;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Default transport-independent executor for local physical indexes.</summary>
public sealed class LocalIndexExecutor(ServerCoreOptions options) : ILocalIndexExecutor
{
    private readonly ServerCoreOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async ValueTask<LocalWriteResult> WriteAsync(OperationContext context, LocalIndexHandle index, BulkDocumentsRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable operationLease = await index.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        if (index.Mode != LocalIndexOpenMode.ReadWrite)
            throw new InvalidOperationException("The selected local index is read-only.");

        List<BulkDocumentResult> results = new(request.Operations.Count);
        int accepted = 0;
        LocalCommitReceipt? receipt = null;
        bool committed = false;
        long lastSequence = 0;
        IndexRuntime runtime = index.Runtime;
        lock (runtime.WriteLock)
        {
            foreach (BulkDocumentOperation operation in request.Operations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(operation.DocumentId))
                {
                    results.Add(new BulkDocumentResult(operation.DocumentId, false, new ApiFailure("invalid_document_id", "Document IDs are required.")));
                    continue;
                }

                switch (operation.Kind)
                {
                    case DocumentOperationKind.Index:
                    case DocumentOperationKind.Update:
                        if (operation.Document is not { ValueKind: System.Text.Json.JsonValueKind.Object } document)
                        {
                            results.Add(new BulkDocumentResult(operation.DocumentId, false, new ApiFailure("invalid_document", "Index and update operations require a JSON object.")));
                            continue;
                        }
                        if (!ServerDocumentMapper.TryMap(operation.DocumentId, document, runtime.Schema, _options.MaximumDocumentBytes, out LeanDocument? mapped, out string code, out string message))
                        {
                            results.Add(new BulkDocumentResult(operation.DocumentId, false, new ApiFailure(code, message)));
                            continue;
                        }
                        runtime.Writer.UpdateDocument(ServerDocumentMapper.DocumentIdField, operation.DocumentId, mapped!);
                        lastSequence = runtime.MarkWrite();
                        accepted++;
                        results.Add(new BulkDocumentResult(operation.DocumentId, true));
                        break;
                    case DocumentOperationKind.Delete:
                        runtime.Writer.DeleteDocuments(new TermQuery(ServerDocumentMapper.DocumentIdField, operation.DocumentId));
                        lastSequence = runtime.MarkWrite();
                        accepted++;
                        results.Add(new BulkDocumentResult(operation.DocumentId, true));
                        break;
                    default:
                        results.Add(new BulkDocumentResult(operation.DocumentId, false, new ApiFailure("invalid_operation", "The document operation is not recognised.")));
                        break;
                }
            }

            if (accepted > 0 && (request.Refresh || request.Durability == RequestedWriteDurability.LocalFsync || runtime.PendingOperations >= _options.MaximumUncommittedOperations))
            {
                CommitResult result = runtime.Commits.Commit(request.Refresh);
                receipt = result switch
                {
                    CommitPublished published => published.Receipt,
                    CommitFailed failed => throw new IOException(failed.Message, failed.Exception),
                    NothingToCommit => null
                };
                committed = receipt is not null;
            }
        }

        using SearcherLease visible = runtime.Searchers.AcquireLease();
        return new LocalWriteResult(results, accepted, committed, receipt, lastSequence, visible.CommitGeneration);
    }

    /// <inheritdoc />
    public async ValueTask<SearchResponse> SearchAsync(OperationContext context, LocalIndexHandle index, SearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable operationLease = await index.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        IndexRuntime runtime = index.Runtime;
        if (request.Size < 1 || request.Size > _options.MaximumSearchResults)
            throw new LocalExecutionException(new ApiFailure("invalid_search_request", "The requested result size is outside the configured limit."));
        if (request.IncludeHighlights)
            throw new LocalExecutionException(new ApiFailure("highlights_not_supported", "Highlights are not available in Community Server 0.1."));
        if (request.Facets is { Count: > 0 } && request.SearchAfter is { Count: > 0 })
            throw new LocalExecutionException(new ApiFailure("unsupported_search", "Search-after cannot be combined with facets in Community Server 0.1."));

        if (!ServerQueryTranslator.TryTranslate(request.Query, runtime.Schema, _options, index.Descriptor.Settings.DefaultField, index.Descriptor.Settings.MaximumQueryClauses, out Query? query, out ApiFailure? queryFailure))
            throw new LocalExecutionException(queryFailure!);
        List<SortField> sorts = BuildSorts(request.Sort, runtime.Schema);
        Stopwatch stopwatch = Stopwatch.StartNew();
        using SearcherLease lease = runtime.Searchers.AcquireLease();
        TopDocs documents;
        IReadOnlyList<EngineFacetResult> engineFacets = [];
        string[] facetFields = [];
        if (request.Facets is { Count: > 0 })
        {
            List<string> fields = [];
            foreach (FacetDefinition facet in request.Facets)
            {
                if (facet.Kind != FacetKind.Terms)
                    throw new LocalExecutionException(new ApiFailure("unsupported_facet", "Only terms facets are available in Community Server 0.1."));
                if (!runtime.Schema.Fields.TryGetValue(facet.Field, out CompiledFieldDefinition? field) || !field.Source.Indexed || field.Source.Type is IndexFieldType.Text or IndexFieldType.Binary or IndexFieldType.Vector)
                    throw new LocalExecutionException(new ApiFailure("invalid_facet_field", $"Field '{facet.Field}' cannot be faceted."));
                fields.Add(facet.Field);
            }
            facetFields = fields.Distinct(StringComparer.Ordinal).ToArray();
        }

        if (facetFields.Length > 0)
            (documents, engineFacets) = lease.Searcher.SearchWithFacets(query!, request.Size, facetFields);
        else if (request.SearchAfter is { Count: > 0 })
        {
            if (!TryDecodeSearchAfter(request.SearchAfter, out ScoreDoc after))
                throw new LocalExecutionException(new ApiFailure("invalid_search_after", "Search-after must contain an internal document ID and score."));
            documents = lease.Searcher.SearchAfter(after, query!, request.Size, sorts.ToArray());
        }
        else
            documents = lease.Searcher.Search(query!, request.Size, sorts, SearchOptions.Default);

        stopwatch.Stop();
        SearchHit[] hits = documents.ScoreDocs.Select(score => ToSearchHit(lease, score, request.IncludeDocuments)).ToArray();
        IReadOnlyList<ServerFacetResult>? facets = request.Facets is { Count: > 0 } ? MapFacets(request.Facets, engineFacets) : null;
        IReadOnlyList<object?>? next = hits.Length == 0 ? null : [documents.ScoreDocs[^1].DocId, documents.ScoreDocs[^1].Score];
        SearchResponse response = new(hits, documents.TotalHits, TotalHitsRelation.Exact, ScoringModel.ShardLocal,
            new ShardSearchSummary(1, documents.IsPartial ? 0 : 1, documents.IsPartial ? 1 : 0, 0),
            new SearchTiming((long)stopwatch.Elapsed.TotalMilliseconds), next, facets, null, documents.IsPartial);
        return response;
    }

    /// <inheritdoc />
    public async ValueTask<ExplainResponse> ExplainAsync(OperationContext context, LocalIndexHandle index, ExplainRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using IDisposable operation = await index.EnterOperationAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.DocumentId))
            throw new LocalExecutionException(new ApiFailure("invalid_document_id", "A document ID is required for explanations."));
        IndexRuntime runtime = index.Runtime;
        if (!ServerQueryTranslator.TryTranslate(request.Query, runtime.Schema, _options, index.Descriptor.Settings.DefaultField, index.Descriptor.Settings.MaximumQueryClauses, out Query? query, out ApiFailure? queryFailure))
            throw new LocalExecutionException(queryFailure!);
        using SearcherLease lease = runtime.Searchers.AcquireLease();
        TopDocs matches = lease.Searcher.Search(new TermQuery(ServerDocumentMapper.DocumentIdField, request.DocumentId), _options.MaximumSearchResults);
        ScoreDoc? document = matches.ScoreDocs.FirstOrDefault();
        if (document is null)
            return new ExplainResponse(false, null, "The document does not exist.");
        Explanation? explanation = query switch
        {
            TermQuery term => lease.Searcher.Explain(term, document.Value.DocId),
            VectorQuery vector => lease.Searcher.Explain(vector, document.Value.DocId),
            _ => null
        };
        if (explanation is null)
            throw new NotSupportedException("Explanations are available for term and vector queries only.");
        return ToExplainResponse(explanation);
    }

    private static SearchHit ToSearchHit(SearcherLease lease, ScoreDoc scoreDocument, bool includeDocument)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> stored = lease.Searcher.GetStoredFields(scoreDocument.DocId);
        string documentId = stored.TryGetValue(ServerDocumentMapper.DocumentIdField, out IReadOnlyList<string>? identifiers) && identifiers.Count > 0 ? identifiers[0] : scoreDocument.DocId.ToString(CultureInfo.InvariantCulture);
        System.Text.Json.JsonElement? document = null;
        if (includeDocument && stored.TryGetValue(ServerDocumentMapper.RawDocumentField, out IReadOnlyList<string>? rawDocuments) && rawDocuments.Count > 0)
        {
            using System.Text.Json.JsonDocument parsed = System.Text.Json.JsonDocument.Parse(rawDocuments[0]);
            document = parsed.RootElement.Clone();
        }
        return new SearchHit(documentId, scoreDocument.Score, document, null, [scoreDocument.DocId, scoreDocument.Score]);
    }

    private static List<SortField> BuildSorts(IReadOnlyList<SortDefinition>? definitions, CompiledIndexSchema schema)
    {
        List<SortField> sorts = [];
        if (definitions is null or { Count: 0 }) { sorts.Add(SortField.Score); sorts.Add(SortField.DocId); return sorts; }
        foreach (SortDefinition definition in definitions)
        {
            bool descending = definition.Direction == SortDirection.Descending;
            if (definition.Field is "_id") { sorts.Add(new SortField(SortFieldType.String, ServerDocumentMapper.DocumentIdField, descending)); continue; }
            if (!schema.Fields.TryGetValue(definition.Field, out CompiledFieldDefinition? field) || !field.Source.Indexed)
                throw new ArgumentException($"Field '{definition.Field}' is not available for sorting.");
            sorts.Add(field.Source.Type switch
            {
                IndexFieldType.Int64 => SortField.Int64(definition.Field, descending),
                IndexFieldType.Double or IndexFieldType.DateTime => SortField.Numeric(definition.Field, descending),
                IndexFieldType.Keyword or IndexFieldType.Boolean => SortField.String(definition.Field, descending),
                _ => throw new ArgumentException($"Field '{definition.Field}' cannot be sorted.")
            });
        }
        if (!sorts.Any(static sort => sort.Type == SortFieldType.DocId)) sorts.Add(SortField.DocId);
        return sorts;
    }

    private static bool TryDecodeSearchAfter(IReadOnlyList<object?> values, out ScoreDoc scoreDoc)
    {
        scoreDoc = default;
        if (values.Count < 2 || !TryConvertInt(values[0], out int docId) || !TryConvertFloat(values[1], out float score) || docId < 0 || !float.IsFinite(score)) return false;
        scoreDoc = new ScoreDoc(docId, score); return true;
    }

    private static bool TryConvertInt(object? value, out int result)
    {
        switch (value) { case int integer: result = integer; return true; case long value64 when value64 is >= int.MinValue and <= int.MaxValue: result = (int)value64; return true; case System.Text.Json.JsonElement json when json.TryGetInt32(out result): return true; default: result = 0; return false; }
    }

    private static bool TryConvertFloat(object? value, out float result)
    {
        switch (value) { case float single: result = single; return true; case double number when double.IsFinite(number): result = (float)number; return float.IsFinite(result); case System.Text.Json.JsonElement json when json.TryGetSingle(out result): return true; default: result = 0; return false; }
    }

    private static IReadOnlyList<ServerFacetResult> MapFacets(IReadOnlyList<FacetDefinition> requested, IReadOnlyList<EngineFacetResult> actual)
    {
        Dictionary<string, EngineFacetResult> byField = actual.ToDictionary(item => item.FieldName, StringComparer.Ordinal);
        List<ServerFacetResult> result = new(requested.Count);
        foreach (FacetDefinition definition in requested)
        {
            if (!byField.TryGetValue(definition.Field, out EngineFacetResult? facet)) { result.Add(new ServerFacetResult(definition.Name, FacetCompleteness.Complete, [])); continue; }
            IEnumerable<Rowles.LeanCorpus.Search.Scoring.FacetBucket> buckets = facet.Buckets;
            if (definition.Size is > 0) buckets = buckets.Take(definition.Size.Value);
            result.Add(new ServerFacetResult(definition.Name, FacetCompleteness.Complete, buckets.Select(bucket => new ServerFacetBucket(bucket.Value, bucket.Count)).ToArray()));
        }
        return result;
    }

    private static ExplainResponse ToExplainResponse(Explanation explanation) => new(explanation.Score > 0f, explanation.Score, explanation.Description, explanation.Details.Select(ToExplainResponse).ToArray());

}
