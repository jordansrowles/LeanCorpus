# Aggregations

Aggregations compute summary statistics over the documents matching a query, in one pass alongside the search.

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

## Types

| Type | Behaviour |
|---|---|
| `AggregationType.Stats` | `Count`, `Min`, `Max`, `Sum`, `Avg` |
| `AggregationType.Histogram` | Fixed-width buckets controlled by `HistogramInterval` (default `10.0`) |
| `AggregationType.Cardinality` | Approximate distinct numeric values via HyperLogLog++ |
| `AggregationType.TDigestPercentiles` | Approximate double percentiles via t-digest |
| `AggregationType.HdrPercentiles` | Approximate Int64 percentiles via HDR histogram |

Histogram results expose buckets through `AggregationResult.Buckets`.

The field must be a numeric doc-values field (`NumericField`).

`Stats.Count` counts observed values, not documents: a multi-valued document
contributes each numeric value. `Cardinality` instead counts distinct values.
HLL++ uses a deterministic 64-bit hash, default precision 14 and an expected
relative standard error of about 0.81%. Set `CardinalityPrecision` from 4 to 18
to trade memory for accuracy.

Use `TDigestPercentiles` for finite double distributions and tail percentiles;
set `Percentiles` as values from 0 to 100 and `TDigestCompression` from 20 to
1,000. Use `HdrPercentiles` for non-negative Int64 measurements such as latency
when an explicit `HdrHighestTrackableValue` and 1–5 significant digits are
known. HDR rejects values above that range rather than silently clamping them.

```csharp
var requests = new[]
{
    new AggregationRequest("users", "user_id", AggregationType.Cardinality)
    {
        CardinalityPrecision = 14
    },
    new AggregationRequest("price", "price", AggregationType.TDigestPercentiles)
    {
        Percentiles = [50, 95, 99],
        TDigestCompression = 100
    },
    new AggregationRequest("latency", "latency_ms", AggregationType.HdrPercentiles)
    {
        HdrHighestTrackableValue = 60_000,
        HdrSignificantDigits = 3,
        Percentiles = [50, 95, 99]
    }
};

var (_, results) = searcher.SearchWithAggregations(query, topN: 20, requests);
var p99 = ((PercentileAggregationResult)results[2]).Percentiles.Single(p => p.Percentile == 99).Value;
```

## See also

- <xref:Rowles.LeanCorpus.Search.Aggregations.AggregationRequest>
- <xref:Rowles.LeanCorpus.Search.Aggregations.AggregationResult>
