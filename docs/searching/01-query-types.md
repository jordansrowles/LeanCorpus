# Query types

Every query derives from `Query`. The built-in types are in `Rowles.LeanCorpus.Search.Queries`.

| Query | Use |
|---|---|
| `TermQuery` | Exact match on one term |
| `BooleanQuery` | Combine clauses with `Must`, `Should`, `MustNot` |
| `PhraseQuery` | Ordered terms within an optional slop |
| `PrefixQuery` | Terms starting with a prefix |
| `WildcardQuery` | `*` and `?` patterns |
| `FuzzyQuery` | Levenshtein, max edits 0–2 |
| `RangeQuery` | Numeric ranges over `NumericField` |
| `RegexpQuery` | .NET regular expressions |
| `DisjunctionMaxQuery` | Best matching clause wins, with tie-breaker |
| `ConstantScoreQuery` | Fixed score; bypass BM25 |
| `FunctionScoreQuery` | Combine BM25 with a numeric field |
| `RrfQuery` | Reciprocal rank fusion of child queries |
| `VectorQuery` | ANN over a vector field |
| `BlockJoinQuery` | Parents whose children match |
| `MoreLikeThisQuery` | Similar documents to a source doc |
| `SpanNearQuery` | Proximity over span queries |
| `GeoBoundingBoxQuery` / `GeoDistanceQuery` | Geo filters |
| `MatchAllDocsQuery` | Every document in the index |
| `MatchNoDocsQuery` | Nothing; sentinel for empty results |
| `FieldExistsQuery` | Documents where a field has a value |
| `TermInSetQuery` | Documents matching any term in a set |
| `PointInSetQuery` | Multi-point set for numeric/doc-values fields |
| `MultiPhraseQuery` | Any of several terms at each phrase position |
| `IntervalsQuery` | Fine-grained positional constraints near terms |
| `CombinedFieldsQuery` | Single query across multiple text fields |

## Run a query

```csharp
var hits = searcher.Search(new TermQuery("title", "fox"), topN: 10);
```

All `Search` overloads return a `TopDocs`.

## See also

- [Boolean queries](02-boolean-queries.md)
- [Disjunction max](08-disjunction-max.md)
- [Query parser](04-query-parser.md)
