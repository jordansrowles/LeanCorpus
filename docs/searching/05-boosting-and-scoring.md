# Boosting and scoring

LeanCorpus defaults to BM25 (`Bm25Similarity`).

## Available similarities

| Similarity | Model | Notes |
|---|---|---|
| `Bm25Similarity` | BM25 (k1=1.2, b=0.75) | Default |
| `Bm25PlusSimilarity` | BM25+ with lower-bound delta | Avoids over-penalising long docs |
| `Bm25LSimilarity` | BM25L with tf/(1+tf) modulated delta | More nuanced lower-bound than BM25+ |
| `TfIdfSimilarity` | Classic TF-IDF | `sqrt(tf) * idf / sqrt(dl)` |
| `TfIdfAugmentedSimilarity` | Augmented TF-IDF | `0.5 + 0.5 * tf/max_tf` |
| `TfIdfDoubleNormSimilarity` | Double-normalised TF-IDF | Two-stage normalisation |
| `TfIdfPivotedSimilarity` | Pivoted TF-IDF | Pivoted document length normalisation |
| `DirichletSimilarity` | LM with Dirichlet smoothing (μ=2000) | Bayesian smoothing towards collection |
| `LMAbsoluteDiscountingSimilarity` | LM with absolute discounting | Subtracts constant δ from counts |
| `LMJelinekMercerSimilarity` | LM with Jelinek-Mercer (λ=0.7) | Linear interpolation with collection |

All implement `ISimilarity`. Set on both writer (for norms) and searcher (for scoring):

```csharp
var config = new IndexWriterConfig { Similarity = new Bm25PlusSimilarity() };
var searcherConfig = new IndexSearcherConfig { Similarity = new Bm25PlusSimilarity() };
```

## Per-query boost

Every `Query` has a `Boost` (default `1.0`). Multiplies that query's contribution within a `BooleanQuery`:

```csharp
var q = new BooleanQuery.Builder()
    .Add(new TermQuery("title", "fox") { Boost = 3.0f }, Occur.Should)
    .Add(new TermQuery("body",  "fox") { Boost = 1.0f }, Occur.Should)
    .Build();
```

## Constant scores

`ConstantScoreQuery` assigns a fixed score; skips BM25:

```csharp
var filter = new ConstantScoreQuery(new TermQuery("status", "published"), score: 1.0f);
```

## Function scores

`FunctionScoreQuery` blends BM25 with a numeric field:

| ScoreMode | Effect |
|---|---|
| `Multiply` (default) | `score * fieldValue` |
| `Replace` | `fieldValue` |
| `Sum` | `score + fieldValue` |
| `Max` | `max(score, fieldValue)` |

```csharp
var boosted = new FunctionScoreQuery(
    new TermQuery("body", "phone"), "popularity", ScoreMode.Multiply);
```

## Index-time field boosting

Set per-field boost factors at write time. They persist in index norms and apply to every query:

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

A field boost of `2.0` makes every hit in that field count twice as much. Set to `0.0` to disable a field for ranking.

## See also

- <xref:Rowles.LeanCorpus.Search.Scoring.Bm25Similarity>
- <xref:Rowles.LeanCorpus.Search.Scoring.ISimilarity>
- <xref:Rowles.LeanCorpus.Search.Queries.ConstantScoreQuery>
- <xref:Rowles.LeanCorpus.Search.Queries.FunctionScoreQuery>
