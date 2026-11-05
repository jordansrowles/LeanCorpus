# Score explanations

`IndexSearcher.Explain` describes why one document received its score. Use it for debugging relevance, validating boosts, and inspecting vector execution. It is not intended for every result in a production response.

## Term scores

```csharp
var query = new TermQuery("body", "corpus");
var results = searcher.Search(query, topN: 10);

foreach (var hit in results.ScoreDocs)
{
    var explanation = searcher.Explain(query, hit.DocId);
    Console.WriteLine(explanation);
}
```

For BM25, the explanation includes the matching term, inverse document frequency, term frequency, field length, average field length, query boost, and index-time boost where present. A non-matching document returns `null`.

Use the global document ID from `ScoreDoc`. Do not substitute a segment-local document ID.

## Vector scores

```csharp
var query = new VectorQuery("embedding", queryVector, k: 20);
var results = searcher.Search(query, topN: 20);

var explanation = searcher.Explain(query, results.ScoreDocs[0].DocId);
Console.WriteLine(explanation);
```

New Float32 vectors and historical Int8 vectors use exact scans, restricted to
documents that pass the query filter. Explanations identify `exact Float32
scan` or `exact decoded Int8 scan`; an old HNSW graph on either representation
is ignored. Int8 scoring is exact for the stored decoded values, not for the
original Float32 vectors.

Historical BBQ queries retain their existing scorer and graph route. BBQ-
containing indexes remain readable but read-only to writers.

## Operational use

Explaining a score repeats query-specific work and may open data not needed by the top-N collector. Sample it for diagnostics or attach it to an explicit debugging endpoint.

The [slow-query log](../observability/02-slow-query-log.md) can include explanations when configured, but enabling that option increases the cost and volume of slow-query records.

Explanations describe the current implementation, not a stable serialisation contract. Parse structured score data from your own application model rather than scraping the explanation text.
