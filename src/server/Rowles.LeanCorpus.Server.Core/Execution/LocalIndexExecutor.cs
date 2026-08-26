using System.Diagnostics;
using System.Globalization;
using System.Text;
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
            if (TryPrepareBulkAdd(runtime, request, out LeanDocument[] bulkDocuments))
            {
                cancellationToken.ThrowIfCancellationRequested();
                runtime.Writer.AddDocuments(bulkDocuments);
                foreach (BulkDocumentOperation operation in request.Operations)
                {
                    lastSequence = runtime.MarkWrite();
                    accepted++;
                    results.Add(new BulkDocumentResult(operation.DocumentId, true));
                }
            }
            else
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

    private bool TryPrepareBulkAdd(IndexRuntime runtime, BulkDocumentsRequest request, out LeanDocument[] documents)
    {
        documents = [];
        if (request.Operations.Count == 0
            || runtime.PendingOperations != 0
            || request.Operations.Any(static operation => operation.Kind is not (DocumentOperationKind.Index or DocumentOperationKind.Update)))
            return false;

        using SearcherLease visible = runtime.Searchers.AcquireLease();
        if (visible.CommitGeneration != runtime.Writer.CurrentCommitGeneration || visible.Searcher.Stats.LiveDocCount != 0)
            return false;

        HashSet<string> ids = new(StringComparer.Ordinal);
        List<LeanDocument> mappedDocuments = new(request.Operations.Count);
        foreach (BulkDocumentOperation operation in request.Operations)
        {
            if (string.IsNullOrWhiteSpace(operation.DocumentId)
                || !ids.Add(operation.DocumentId)
                || operation.Document is not { ValueKind: System.Text.Json.JsonValueKind.Object } document
                || !ServerDocumentMapper.TryMap(operation.DocumentId, document, runtime.Schema, _options.MaximumDocumentBytes, out LeanDocument? mapped, out _, out _))
                return false;
            mappedDocuments.Add(mapped!);
        }

        documents = mappedDocuments.ToArray();
        return true;
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
        else if (request.SearchAfter is not null)
        {
            if (!TryDecodeSearchAfter(request.SearchAfter, sorts, out SearchAfterValue[] after))
                throw new LocalExecutionException(new ApiFailure("invalid_search_after", "Search-after is not a valid cursor for the requested sort order."));
            documents = lease.Searcher.SearchAfter(after, query!, request.Size, sorts);
        }
        else
            documents = lease.Searcher.Search(query!, request.Size, sorts, SearchOptions.Default);

        stopwatch.Stop();
        SearchHit[] hits = documents.ScoreDocs.Select(score => ToSearchHit(lease, score, request.IncludeDocuments, sorts)).ToArray();
        IReadOnlyList<ServerFacetResult>? facets = request.Facets is { Count: > 0 } ? MapFacets(request.Facets, engineFacets) : null;
        IReadOnlyList<object?>? next = hits.Length == 0 ? null : CreateSearchAfter(lease, documents.ScoreDocs[^1], sorts);
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

    private static SearchHit ToSearchHit(
        SearcherLease lease,
        ScoreDoc scoreDocument,
        bool includeDocument,
        IReadOnlyList<SortField> sorts)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> stored = lease.Searcher.GetStoredFields(scoreDocument.DocId);
        string documentId = stored.TryGetValue(ServerDocumentMapper.DocumentIdField, out IReadOnlyList<string>? identifiers) && identifiers.Count > 0 ? identifiers[0] : scoreDocument.DocId.ToString(CultureInfo.InvariantCulture);
        System.Text.Json.JsonElement? document = null;
        if (includeDocument && stored.TryGetValue(ServerDocumentMapper.RawDocumentField, out IReadOnlyList<string>? rawDocuments) && rawDocuments.Count > 0)
        {
            using System.Text.Json.JsonDocument parsed = System.Text.Json.JsonDocument.Parse(rawDocuments[0]);
            document = parsed.RootElement.Clone();
        }
        IReadOnlyList<object?> sortValues = lease.Searcher.CaptureSortValues(scoreDocument, sorts).Select(ToPublicSortValue).ToArray();
        return new SearchHit(documentId, scoreDocument.Score, document, null, sortValues);
    }

    private static List<SortField> BuildSorts(IReadOnlyList<SortDefinition>? definitions, CompiledIndexSchema schema)
    {
        List<SortField> sorts = [];
        if (definitions is null or { Count: 0 })
        {
            sorts.Add(SortField.Score);
            sorts.Add(SortField.String(ServerDocumentMapper.DocumentIdField));
            return sorts;
        }
        foreach (SortDefinition definition in definitions)
        {
            bool descending = definition.Direction == SortDirection.Descending;
            if (definition.Field is "_id") { sorts.Add(new SortField(SortFieldType.String, ServerDocumentMapper.DocumentIdField, descending)); continue; }
            if (!schema.Fields.TryGetValue(definition.Field, out CompiledFieldDefinition? field) || !field.Source.Indexed)
                throw new ArgumentException($"Field '{definition.Field}' is not available for sorting.");
            sorts.Add(field.Source.Type switch
            {
                IndexFieldType.Int64 => SortField.Int64(definition.Field, descending),
                IndexFieldType.Double => SortField.Numeric(definition.Field, descending),
                IndexFieldType.DateTime => SortField.Int64(definition.Field, descending),
                IndexFieldType.Keyword or IndexFieldType.Boolean => SortField.String(definition.Field, descending),
                _ => throw new ArgumentException($"Field '{definition.Field}' cannot be sorted.")
            });
        }
        if (!sorts.Any(sort => sort.Type == SortFieldType.String && sort.FieldName == ServerDocumentMapper.DocumentIdField))
            sorts.Add(SortField.String(ServerDocumentMapper.DocumentIdField));
        return sorts;
    }

    private static IReadOnlyList<object?> CreateSearchAfter(
        SearcherLease lease,
        ScoreDoc scoreDocument,
        IReadOnlyList<SortField> sorts)
    {
        SearchAfterValue[] values = lease.Searcher.CaptureSortValues(scoreDocument, sorts);
        object?[] cursor = new object?[values.Length + 2];
        cursor[0] = 1;
        cursor[1] = SortIdentity(sorts);
        for (int i = 0; i < values.Length; i++)
            cursor[i + 2] = ToPublicSortValue(values[i]);
        return cursor;
    }

    private static bool TryDecodeSearchAfter(
        IReadOnlyList<object?> values,
        IReadOnlyList<SortField> sorts,
        out SearchAfterValue[] afterValues)
    {
        afterValues = [];
        if (values.Count != sorts.Count + 2 || !TryConvertInt(values[0], out int version) || version != 1)
            return false;
        if (!TryConvertString(values[1], out string? shape) || !string.Equals(shape, SortIdentity(sorts), StringComparison.Ordinal))
            return false;

        var decoded = new SearchAfterValue[sorts.Count];
        for (int i = 0; i < sorts.Count; i++)
        {
            object? value = values[i + 2];
            SortField sort = sorts[i];
            switch (sort.Type)
            {
                case SortFieldType.Score:
                case SortFieldType.Numeric:
                    if (!TryConvertDouble(value, out double number)) return false;
                    decoded[i] = SearchAfterValue.FromNumeric(sort.Type, number);
                    break;
                case SortFieldType.DocId:
                case SortFieldType.Int64:
                    if (!TryConvertLong(value, out long integer)) return false;
                    decoded[i] = SearchAfterValue.FromInt64(sort.Type, integer);
                    break;
                case SortFieldType.String:
                    if (!TryConvertString(value, out string? text) || text is null) return false;
                    decoded[i] = SearchAfterValue.FromString(text);
                    break;
                default:
                    return false;
            }
        }

        afterValues = decoded;
        return true;
    }

    private static string SortIdentity(IReadOnlyList<SortField> sorts)
    {
        var builder = new StringBuilder();
        foreach (SortField sort in sorts)
        {
            builder.Append((int)sort.Type)
                .Append(':')
                .Append(sort.Descending ? '1' : '0')
                .Append(':')
                .Append((int)sort.Selector)
                .Append(':')
                .Append(sort.FieldName.Length)
                .Append(':')
                .Append(sort.FieldName)
                .Append(';');
        }
        return builder.ToString();
    }

    private static object ToPublicSortValue(SearchAfterValue value) => value.Type switch
    {
        SortFieldType.Score or SortFieldType.Numeric => value.NumericValue,
        SortFieldType.DocId or SortFieldType.Int64 => value.Int64Value,
        SortFieldType.String => value.StringValue!,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Type, "The sort value type is not supported.")
    };

    private static bool TryConvertInt(object? value, out int result)
    {
        switch (value)
        {
            case int integer:
                result = integer;
                return true;
            case long value64 when value64 is >= int.MinValue and <= int.MaxValue:
                result = (int)value64;
                return true;
            case System.Text.Json.JsonElement json when json.TryGetInt32(out result):
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryConvertLong(object? value, out long result)
    {
        switch (value)
        {
            case long integer:
                result = integer;
                return true;
            case int value32:
                result = value32;
                return true;
            case System.Text.Json.JsonElement json when json.TryGetInt64(out result):
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryConvertDouble(object? value, out double result)
    {
        switch (value)
        {
            case double number when double.IsFinite(number):
                result = number;
                return true;
            case float single when float.IsFinite(single):
                result = single;
                return true;
            case decimal decimalValue:
                result = (double)decimalValue;
                return double.IsFinite(result);
            case System.Text.Json.JsonElement json when json.TryGetDouble(out result):
                return double.IsFinite(result);
            default:
                result = 0;
                return false;
        }
    }

    private static bool TryConvertString(object? value, out string? result)
    {
        switch (value)
        {
            case string text:
                result = text;
                return true;
            case System.Text.Json.JsonElement json when json.ValueKind == System.Text.Json.JsonValueKind.String:
                result = json.GetString();
                return true;
            default:
                result = null;
                return false;
        }
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
