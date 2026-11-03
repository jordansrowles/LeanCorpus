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

Vector explanations report cosine similarity and the selected execution strategy. Depending on the segment and filter, that may be flat search, HNSW candidate generation and rescoring, brute-force filtered search, HNSW pre-filtering, or HNSW post-filtering. For quantised fields, scores are approximate to the original Float32 vectors.

Approximate traversal diagnostics can include `ef`, shortlist size, and graph node count. The final score uses the stored Float32 vector where available, or the quantised representation for Int8 and historical BBQ fields. BBQ-containing indexes remain readable but read-only to writers.

## Operational use

Explaining a score repeats query-specific work and may open data not needed by the top-N collector. Sample it for diagnostics or attach it to an explicit debugging endpoint.

The [slow-query log](../observability/02-slow-query-log.md) can include explanations when configured, but enabling that option increases the cost and volume of slow-query records.

Explanations describe the current implementation, not a stable serialisation contract. Parse structured score data from your own application model rather than scraping the explanation text.
