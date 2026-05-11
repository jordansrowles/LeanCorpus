# Query types overview

Every query derives from <xref:Rowles.LeanCorpus.Search.Query>. The built-in types live
under `Rowles.LeanCorpus.Search.Queries`.

| Query | Use |
|---|---|
| `TermQuery` | exact match on one term |
| `BooleanQuery` | combine clauses with `Must` / `Should` / `MustNot` |
| `PhraseQuery` | ordered terms within an optional slop |
| `PrefixQuery` | terms starting with a prefix |
| `WildcardQuery` | `*` and `?` patterns |
| `FuzzyQuery` | Levenshtein, max edits 0–2 |
| `RangeQuery` | numeric ranges over `NumericField` |
| `RegexpQuery` | .NET regular expressions |
| `ConstantScoreQuery` | wrap to bypass BM25 |
| `FunctionScoreQuery` | combine BM25 with a numeric field |
| `RrfQuery` | reciprocal rank fusion of children |
| `VectorQuery` | ANN over a vector field |
| `BlockJoinQuery` | parents whose children match |
| `MoreLikeThisQuery` | similar documents to a source doc |
| `SpanNearQuery` | proximity over span queries |
| `GeoBoundingBoxQuery` / `GeoDistanceQuery` | geo filters |

## Running a query

```csharp
var hits = searcher.Search(new TermQuery("title", "fox"), topN: 10);
```

All overloads of <xref:Rowles.LeanCorpus.Search.Searcher.IndexSearcher.Search%2A>
return a `TopDocs`.

## See also

- [Boolean queries](02-boolean-queries.md)
- [Query parser](04-query-parser.md)
