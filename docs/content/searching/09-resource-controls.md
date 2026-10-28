# Per-query resource controls

Every `Search` overload accepts an optional `SearchOptions` that bounds the resources a single query can consume. Use these to prevent runaway queries from exhausting memory or blocking threads.

## Timeout

```csharp
var hits = searcher.Search(query, topN: 10, new SearchOptions
{
    Timeout = TimeSpan.FromMilliseconds(500),
});
```

If the timeout fires before the search completes, partial results are returned and `TopDocs.IsPartial` is set to `true`. Materialised score and ordinary sorted searches check before precomputation and between segments. A segment query already in progress may finish; subsequent segments are skipped. Packed spatial nearest traversal also checks during its specialised BKD and missing-value scans.

## Memory budget

```csharp
var hits = searcher.Search(query, topN: 100, new SearchOptions
{
    MaxResultBytes = 16 * 1024 * 1024, // 16 MB
});
```

For a regular top-N search, the requested candidate heap must fit before execution begins. LeanCorpus estimates one retained `ScoreDoc` at roughly 12 bytes and throws `ArgumentException` when `topN * EstimatedBytes` exceeds the budget. Single-field and multi-field sorted searches retain at most `topN` candidates and their sort values, instead of materialising every match first.

Streaming searches apply the budget between segments and stop yielding when the next per-segment result heap would exceed it. The limit is approximate: it does not include every query, scorer, filter bitmap, sort-value buffer, or codec allocation.

## Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
var hits = searcher.Search(query, topN: 10, new SearchOptions
{
    CancellationToken = cts.Token,
});
```

When cancellation is supplied through `SearchOptions`, sorted searches return partial results with `TopDocs.IsPartial = true`, including an empty partial result when the token is already cancelled. The token is checked between segments. Combine with `Timeout` for a deadline plus external cancellation. Search overloads that accept a `CancellationToken` directly retain their throwing cancellation behaviour.

## Partial results

```csharp
if (hits.IsPartial)
    Console.WriteLine($"Search timed out; {hits.TotalHits} hits so far");
```

For materialised score and ordinary sorted top-N searches, `IsPartial` is set when timeout or cancellation stops execution before precomputation or between segments. `TotalHits` counts matches established by work completed before stopping. Index-sort early termination also reports partial results on normal completion because it observes only the bounded candidates needed from each segment. An undersized result budget is rejected before searching rather than returned as partial.

Cross-segment coordinators for `MoreLikeThisQuery`, `RrfQuery`, and `BlockJoinQuery` keep their existing execution and candidate-materialisation behaviour; their query coordination is not bounded by the sorted top-N collector.

## Streaming results

For pipelines that process results as they arrive rather than collecting a top-N:

```csharp
foreach (var hit in searcher.SearchStreaming(query, perSegmentTopN: 1_024, options: new SearchOptions
{
    Timeout = TimeSpan.FromSeconds(5),
    CancellationToken = ctx.Token,
}))
{
    ProcessHit(hit);
}
```

`SearchStreaming` yields `ScoreDoc` results segment by segment as they are scored. Results within a segment are ordered by score; results across segments are not globally sorted. Use for bulk re-scoring, export pipelines, or feeding a downstream ranker.

## Async streaming

```csharp
await foreach (var hit in searcher.SearchAsync(query, new SearchOptions
{
    Timeout = TimeSpan.FromSeconds(3),
}, ctx.Token))
{
    await ProcessHitAsync(hit);
}
```

`SearchAsync` is the async counterpart of `SearchStreaming`. It yields `ScoreDoc` results segment by segment as they are scored. Results within a segment are ordered by score; results across segments are not globally sorted. External cancellation throws `OperationCanceledException`; timeout or budget exhaustion ends the stream.

## Convenience factories

```csharp
var budgeted = SearchOptions.WithBudget(16 * 1024 * 1024);
var timed = SearchOptions.WithTimeout(TimeSpan.FromMilliseconds(500));
var bounded = SearchOptions.WithBudgetAndTimeout(
    16 * 1024 * 1024,
    TimeSpan.FromMilliseconds(500));
```

Use an object initializer when cancellation or `StreamResults` is also required.

## Custom collectors

`IndexSearcher.Search(Query, ICollector)` supports custom collection, and `TopNCollectorWrapper` adapts the built-in top-N collector to `ICollector`. That overload does not accept `SearchOptions`; a custom collector must implement any additional resource policy it requires.

## See also

- [Concurrent indexing](../concurrency/02-concurrent-indexing.md)
- [Searcher manager](../concurrency/01-searcher-manager.md)
