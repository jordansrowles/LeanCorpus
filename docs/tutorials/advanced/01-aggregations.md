# Aggregations

Aggregations compute summary statistics over the documents matching a query, in one
pass alongside the search.

## Stats and histograms

```csharp
using Rowles.LeanCorpus.Search.Aggregations;

var aggs = new[]
{
    new AggregationRequest("price_stats", "price"),
    new AggregationRequest("price_hist",  "price", AggregationType.Histogram)
    {
        HistogramInterval = 10.0
    },
};

var (hits, results) = searcher.SearchWithAggregations(query, topN: 20, aggs);

foreach (var r in results)
    Console.WriteLine($"{r.Name}: count={r.Count} avg={r.Avg} min={r.Min} max={r.Max}");
```

## Available types

- `AggregationType.Stats` — `Count`, `Min`, `Max`, `Sum`, `Avg`.
- `AggregationType.Histogram` — fixed-width buckets controlled by
  `HistogramInterval` (default `10.0`).

Histogram results expose the buckets through `AggregationResult.Buckets`.

## Field requirements

The `Field` must be a numeric doc-values field (i.e., a `NumericField`).

## See also

- <xref:Rowles.LeanCorpus.Search.Aggregations.AggregationRequest>
- <xref:Rowles.LeanCorpus.Search.Aggregations.AggregationResult>
