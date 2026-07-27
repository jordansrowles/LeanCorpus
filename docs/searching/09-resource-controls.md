# Per-query resource controls

Every `Search` overload accepts an optional `SearchOptions` that bounds the resources a single query can consume. Use these to prevent runaway queries from exhausting memory or blocking threads.

## Timeout

```csharp
var hits = searcher.Search(query, topN: 10, new SearchOptions
{
    Timeout = TimeSpan.FromMilliseconds(500),
});
```

If the timeout fires before the search completes, partial results are returned and `TopDocs.IsPartial` is set to `true`. The search is cancelled cooperatively between segments — a segment that has already started scoring will finish, but subsequent segments are skipped.

## Memory budget

```csharp
var hits = searcher.Search(query, topN: 100, new SearchOptions
{
    MaxResultBytes = 16 * 1024 * 1024, // 16 MB
});
```

For a regular top-N search, the requested heap must fit before execution begins. LeanCorpus estimates one retained `ScoreDoc` at roughly 12 bytes and throws `ArgumentException` when `topN * EstimatedBytes` exceeds the budget.

Streaming searches apply the budget between segments and stop yielding when the next per-segment result heap would exceed it. The limit is approximate and does not include every query, scorer, or codec allocation.

## Cancellation

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
var hits = searcher.Search(query, topN: 10, new SearchOptions
{
    CancellationToken = cts.Token,
});
```

The cancellation token is checked between segments. If cancelled, partial results are returned with `TopDocs.IsPartial = true`. Combine with `Timeout` for a hard deadline plus external cancellation.

## Partial results

```csharp
if (hits.IsPartial)
    Console.WriteLine($"Search timed out; {hits.TotalHits} hits so far");
```

For materialised top-N search, `IsPartial` is set when timeout or cancellation stops execution between segments. An undersized result budget is rejected before searching rather than returned as partial.

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
