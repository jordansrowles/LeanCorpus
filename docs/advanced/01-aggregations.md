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
| `AggregationType.Cardinality` | Approximate distinct numeric values via a bounded HLL-style register sketch |
| `AggregationType.TDigestPercentiles` | Approximate double percentiles via t-digest |
| `AggregationType.HdrPercentiles` | Approximate non-negative Int64 percentiles via an HDR-style logarithmic histogram |

Histogram results expose buckets through `AggregationResult.Buckets`. They retain
bucket counts while matching documents are collected, rather than buffering raw
observations. Non-finite values are rejected for histograms, and a requested
bucket span beyond the configured 100,000-bucket safety limit fails instead of
clamping an observation into a false bucket.

The field must be a numeric doc-values field (`NumericField`).

`Stats.Count` counts observed values, not documents: a multi-valued document
contributes each numeric value. `Cardinality` instead counts distinct values.
Cardinality uses a deterministic 64-bit hash and sparse-to-dense register
storage. The default precision is 14, with an expected relative standard error
of about 0.81%. Set `CardinalityPrecision` from 4 to 18 to trade memory for
accuracy. Results identify this implementation as `hll-style-sparse-dense`.

Use `TDigestPercentiles` for finite double distributions and tail percentiles;
set `Percentiles` as values from 0 to 100 and `TDigestCompression` from 20 to
1,000. Use `HdrPercentiles` for non-negative Int64 measurements such as latency
when an explicit `HdrHighestTrackableValue` and 1–5 significant digits are
known. This HDR-style logarithmic histogram is identified as
`hdr-style-logarithmic` and rejects values above the configured range rather
than silently clamping them.

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

## Spatial aggregations

Numeric and spatial requests can be submitted together through the
`ISearchAggregationRequest` overload. The searcher evaluates them in one
matching-document side traversal and returns results in request order.

Geo distance ranges aggregate every `GeoPointField` value in metres. The lower
bound is inclusive and the upper bound is exclusive; ranges may overlap, and a
document counts once in each bucket even when several of its points match.
Geo centroid and bounds requests accept Geo point or shape fields. Centroids
select the highest contributing dimension (area, then line, then point), reset
lower-dimensional totals when that dimension rises, and weight shape lines by
segment length and areas by triangle area. Geo bounds can return a narrow Date
Line crossing interval and use the union of shape primitive longitude
intervals. Multi-valued points are read exactly, with `_lat`/`_lon` fallback
for legacy segments.

Shape aggregations require Shape DocValues on every document containing that
shape field in each segment. Shape fields enable them by default; setting
`storeDocValues: false` retains relation queries but makes shape centroid and
bounds requests fail with `InvalidOperationException` before query traversal.
Missing fields contribute no value.

## See also

- <xref:Rowles.LeanCorpus.Search.Aggregations.AggregationRequest>
- <xref:Rowles.LeanCorpus.Search.Aggregations.AggregationResult>
