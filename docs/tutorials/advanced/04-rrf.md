# Reciprocal rank fusion

`RrfQuery` merges several result lists by rank, not by score, so the children do not
need score normalisation.

## Score formula

```text
score(d) = Σ 1 / (k + rank_i(d))
```

Where `rank_i(d)` is the 1-based position of document `d` in child query `i`'s
results. `k` defaults to `60`.

## Combining text and vector results

```csharp
using Rowles.LeanCorpus.Search.Queries;

var rrf = new RrfQuery(k: 60)
    .Add(new TermQuery("body", "machine"))
    .Add(new VectorQuery("embedding", queryVector, topK: 50));

var hits = searcher.Search(rrf, topN: 10);
```

## Tuning k

Higher `k` flattens the contribution from top-ranked documents. The default of `60`
is the value used in the original RRF paper.

## Combining pre-computed TopDocs

For ad-hoc fusion (e.g., results from external systems), use the static helper:

```csharp
var fused = RrfQuery.Combine(new[] { topDocsA, topDocsB }, topN: 10, k: 60);
```

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.RrfQuery>
