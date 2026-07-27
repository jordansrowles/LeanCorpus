# Query types

Every query derives from `Query`. Built-in queries live in `Rowles.LeanCorpus.Search.Queries`.

Choose the narrowest query that expresses the requirement. Exact term and point queries can seek directly to compact index structures. Patterns, broad alternatives, and positional trees do more expansion or verification work.

## Exact terms and sets

| Query | Use it for | Notes |
|---|---|---|
| `TermQuery` | One exact indexed term | Input is not analysed automatically. Best basic primitive for exact or pre-analysed terms. |
| `TermInSetQuery` | Any term from a set | Prefer it to a very large Boolean OR of term queries. |
| `FieldExistsQuery` | Documents with a value | Uses indexed field data rather than stored-field retrieval. |
| `MatchAllDocsQuery` | Every live document | Often combined with filters, sorting, or aggregations. |
| `MatchNoDocsQuery` | No documents | Useful as an explicit empty rewrite result. |

```csharp
var one = new TermQuery("status", "published");
var any = new TermInSetQuery("category", ["books", "music", "games"]);
```

## Compound and scoring queries

| Query | Use it for | Notes |
|---|---|---|
| `BooleanQuery` | Required, optional, and excluded clauses | Use `Must`, `Should`, and `MustNot`; see [Boolean queries](02-boolean-queries.md). |
| `DisjunctionMaxQuery` | Best matching field or clause | Adds a configurable fraction of scores from other matching clauses. |
| `CombinedFieldsQuery` | One analysed term set across several text fields | Useful when fields represent one logical body with different weights. |
| `ConstantScoreQuery` | Matching without similarity-based score variation | Wraps another query and assigns a fixed score. |
| `FunctionScoreQuery` | Combine a query score with a numeric field | Useful for recency, popularity, or business signals. |
| `RrfQuery` | Fuse independently ranked child queries | Reciprocal rank fusion avoids comparing unlike score scales directly. |

## Phrase, span, and intervals

These queries require indexed positions.

| Query | Use it for | Notes |
|---|---|---|
| `PhraseQuery` | Ordered terms with optional gaps | The simplest phrase form. |
| `MultiPhraseQuery` | Alternatives at one or more phrase positions | Supports explicit positions and slop. |
| `SpanTermQuery` | A term represented as a span | Leaf for other span queries. |
| `SpanNearQuery` | Ordered or unordered span proximity | Composes span leaves or other span queries. |
| `SpanOrQuery` | Alternative spans | All clauses must target the same field. |
| `SpanNotQuery` | Include spans that do not overlap excluded spans | Exclusion is positional, not merely document-level. |
| `IntervalsQuery` | Ordered, unordered, alternative, containment, or exclusion trees | Best for a complex positional expression. |

See [Phrase and proximity](03-phrase-and-proximity.md) and [Intervals](10-intervals.md).

## Multi-term text queries

| Query | Use it for | Cost controls |
|---|---|---|
| `PrefixQuery` | Terms beginning with a fixed prefix | Longer fixed prefixes reduce dictionary expansion. |
| `WildcardQuery` | `*` and `?` patterns | Avoid leading wildcards on large term dictionaries. |
| `RegexpQuery` | Regular-expression term matching | Bound user patterns and test worst cases. |
| `FuzzyQuery` | Terms within edit distance 0 to 2 | `MaxExpansions` limits candidate terms. |
| `TermRangeQuery` | Lexicographic term range | Bounds may be inclusive, exclusive, or `null` for unbounded. |

```csharp
var names = new TermRangeQuery(
    "surname",
    lowerTerm: "m",
    upperTerm: "r",
    includeLower: true,
    includeUpper: false);
```

`TermRangeQuery` compares indexed terms lexicographically. It is not a numeric range query and `"100"` does not sort numerically before `"20"`.

## Numeric and point queries

| Query | Use it for | Notes |
|---|---|---|
| `RangeQuery` | Inclusive `double` range over `NumericField` | BKD-backed. |
| `Int64RangeQuery` | Inclusive 64-bit integer range | Preserves integer precision. |
| `PointInSetQuery` | Any `double` value from a set | Useful for non-contiguous numeric filters. |
| `Int64PointInSetQuery` | Any 64-bit integer from a set | Avoids conversion through `double`. |

```csharp
var price = new RangeQuery("price", min: 10.0, max: 25.0);
var ids = new Int64PointInSetQuery("accountId", [12L, 48L, 91L]);
```

## Specialised queries

| Query | Use it for | Guide |
|---|---|---|
| `VectorQuery` | Exact or HNSW nearest-neighbour search, optionally filtered | [Vector search](../advanced/05-vector-search.md) |
| `BlockJoinQuery` | Parent documents whose child documents match | [Block join](../advanced/06-block-join.md) |
| `MoreLikeThisQuery` | Documents similar to source text or a source document | [More Like This](../advanced/07-more-like-this.md) |
| `GeoBoundingBoxQuery` | Points inside a latitude/longitude rectangle | [Geo search](../advanced/10-geo-search.md) |
| `GeoDistanceQuery` | Points within a radius | [Geo search](../advanced/10-geo-search.md) |

## Run a query

```csharp
var query = new TermQuery("title", "fox");
var hits = searcher.Search(query, topN: 10);
```

`Search` returns `TopDocs`, containing total-hit information and ordered `ScoreDoc` values. Query boosts affect scoring and form part of query-cache identity.

For user query strings, use the [query parser](04-query-parser.md) or analyse input deliberately. Constructors generally expect indexed terms, not raw natural-language text.
