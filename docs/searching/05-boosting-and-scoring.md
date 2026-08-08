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

For composed numeric fields, constants, and query scores, pass a
`DoubleValuesSource` instead:

```csharp
var source = DoubleValuesSource.FromDoubleField("popularity")
    .Add(DoubleValuesSource.Constant(1));
var boosted = new FunctionScoreQuery(
    new TermQuery("body", "phone"), source, ScoreMode.Multiply);
```

`FunctionQuery` uses a value source as the score for every live document.
Derive from `DoubleValuesSource` for application-specific freshness or
distance calculations.

## Index-time field boosting

Set a boost on each indexed field value. It persists in segment norms and applies to matching queries:

```csharp
var document = new LeanDocument();
document.Add(new TextField(
    "title",
    "A compact corpus",
    stored: true,
    boost: 3.0f));
document.Add(new TextField(
    "body",
    "Searchable article text",
    stored: true,
    boost: 1.0f));
```

A field boost must be finite and greater than zero. Use an unindexed stored field or a separate filter-only field when content must not contribute to ranking.

## Block-Max WAND

Block-Max WAND can skip postings blocks whose score upper bound cannot enter the current top-N:

```csharp
var searcherConfig = new IndexSearcherConfig
{
    EnableBlockMaxWand = true,
};
```

The current optimised path applies to should-only Boolean term queries when every postings stream has block metadata and there are no `MustNot` clauses. Other shapes fall back to exhaustive scoring.

WAND changes work performed, not the intended result ordering or scores. Validate parity against the disabled path and benchmark broad disjunctions with a small top-N. Selective queries or large requested result sets may not benefit enough to offset bound management.

Use [score explanations](11-score-explanations.md) for individual factors and [search internals](../contributors/search-internals.md) for the skipping model.

## See also

- <xref:Rowles.LeanCorpus.Search.Scoring.Bm25Similarity>
- <xref:Rowles.LeanCorpus.Search.Scoring.ISimilarity>
- <xref:Rowles.LeanCorpus.Search.Queries.ConstantScoreQuery>
- <xref:Rowles.LeanCorpus.Search.Queries.FunctionScoreQuery>
