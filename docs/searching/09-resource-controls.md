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

`MaxResultBytes` caps the total bytes of intermediate results. When the budget is exhausted, collection stops and `TopDocs.IsPartial` is set. Useful for high-cardinality queries that would otherwise allocate a large priority queue.

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

`IsPartial` is set whenever a search stops early — timeout, memory budget, or cancellation. It does not distinguish the cause; check your `SearchOptions` configuration to determine why.

## Streaming results

For pipelines that process results as they arrive rather than collecting a top-N:

```csharp
foreach (var hit in searcher.SearchStreaming(query, new SearchOptions
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

`SearchAsync` is the async counterpart of `SearchStreaming` — it yields `ScoreDoc` results segment by segment as they are scored. Results within a segment are ordered by score; results across segments are not globally sorted. Accepts the same `SearchOptions` as `Search`.
## Segment-level wrapping

For advanced use, `TopNCollectorWrapper` lets you wrap a per-segment collector:

```csharp
var options = new SearchOptions
{
    Timeout = TimeSpan.FromSeconds(1),
};
var collector = new TopNCollectorWrapper(topN, options);
var hits = searcher.Search(query, collector);
```

This is what `Search` uses internally. Use it when you need a custom collector that still benefits from timeout, budget, and cancellation checks.

## See also

- [Concurrent indexing](../concurrency/02-concurrent-indexing.md)
- [Searcher manager](../concurrency/01-searcher-manager.md)
