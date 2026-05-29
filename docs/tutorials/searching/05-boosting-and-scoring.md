# Boosting and scoring

LeanCorpus scores with BM25 by default
(<xref:Rowles.LeanCorpus.Search.Scoring.Bm25Similarity>).

## Per-query boost

Every `Query` has a `Boost` (default `1.0`). Multiplies the contribution of that
query within a `BooleanQuery`.

```csharp
var q = new BooleanQuery.Builder()
    .Add(new TermQuery("title", "fox") { Boost = 3.0f }, Occur.Should)
    .Add(new TermQuery("body",  "fox") { Boost = 1.0f }, Occur.Should)
    .Build();
```

## Constant scores

`ConstantScoreQuery` assigns a fixed score and skips BM25 entirely. Useful for
filters where ranking is irrelevant.

```csharp
var filter = new ConstantScoreQuery(new TermQuery("status", "published"), score: 1.0f);
```

## Function scores

`FunctionScoreQuery` blends BM25 with a numeric field via a `ScoreMode`:

| Mode | Effect |
|---|---|
| `Multiply` (default) | `score * fieldValue` |
| `Replace` | `fieldValue` |
| `Sum` | `score + fieldValue` |
| `Max` | `max(score, fieldValue)` |

```csharp
var inner = new TermQuery("body", "phone");
var boosted = new FunctionScoreQuery(inner, "popularity", ScoreMode.Multiply);
```

## Index-time field boosting

Per-field boost factors can be set at write time through `IndexWriterConfig.FieldBoosts`
and are written into the index norms. They multiply the query-time BM25 score just like
`Boost` on a `Query`, but persist with the field so they apply to every query:

```csharp
var config = new IndexWriterConfig
{
    FieldBoosts = new Dictionary<string, float>
    {
        ["title"] = 3.0f,
        ["body"]  = 1.0f,
    },
};
```

A field boost of `2.0` makes every hit in that field count twice as much. Set it to
`0.0` to effectively disable a field for ranking.


## Custom similarity

Set `IndexWriterConfig.Similarity` (writer-time norms) and
`IndexSearcherConfig.Similarity` (query-time scoring) to swap in a different
implementation.

## See also
- <xref:Rowles.LeanCorpus.Search.Queries.ConstantScoreQuery>
- <xref:Rowles.LeanCorpus.Search.Queries.FunctionScoreQuery>
- <xref:Rowles.LeanCorpus.Search.Queries.ScoreMode>
