# Reciprocal rank fusion

`RrfQuery` merges result lists by rank, not score. Children don't need score normalisation.

## Formula

```text
score(d) = Σ 1 / (k + rank_i(d))
```

`rank_i(d)` is the 1-based position of document `d` in child query `i`'s results. `k` defaults to `60`.

## Combining text and vector results

```csharp
var rrf = new RrfQuery(k: 60)
    .Add(new TermQuery("body", "machine"))
    .Add(new VectorQuery("embedding", queryVector, topK: 50));

var hits = searcher.Search(rrf, topN: 10);
```

`RrfQuery` runs each child as an independent top-level search, so it must be the
root of a search. Using it as a Boolean clause, filter or block-join child
throws `NotSupportedException`.

## Tuning k

Higher `k` flattens the contribution from top-ranked documents. Default `60` is from the original RRF paper.

## Combining pre-computed TopDocs

```csharp
var fused = RrfQuery.Combine(new[] { topDocsA, topDocsB }, topN: 10, k: 60);
```

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.RrfQuery>
