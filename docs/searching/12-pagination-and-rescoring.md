# Pagination and rescoring

## Search-after pagination

Use the last hit from one page as the cursor for the next page:

```csharp
var query = new TermQuery("body", "corpus");
var sort = SortField.Numeric("published", descending: true);

TopDocs first = searcher.Search(query, 25, sort);
TopDocs second = searcher.SearchAfter(first.ScoreDocs[^1], query, 25, sort);
```

Score order, document-ID order, single-field sorts and multi-field sorts are
supported. The cursor must come from the same `IndexSearcher` snapshot and the
same query and sort definition. `SearchAfter` counts every match but retains
only the requested next-page candidates for segment-local query families.
Queries that coordinate complete result sets across segments retain that
coordination step.

## Second-pass query rescoring

`QueryRescorer` reranks an existing candidate set without adding documents:

```csharp
TopDocs firstPass = searcher.Search(new TermQuery("body", "search"), 100);
var rescorer = new QueryRescorer(
    new PhraseQuery("body", ["full", "text"]),
    firstPassWeight: 1,
    secondPassWeight: 3);

TopDocs reranked = rescorer.Rescore(searcher, firstPass, topN: 20);
```

Derive from `QueryRescorer` and override `Combine` when ranking needs a custom
score combination.

## Function values

`DoubleValuesSource` supplies numeric values from fields, constants or the
current score. Sources compose without external dependencies:

```csharp
DoubleValuesSource popularity =
    DoubleValuesSource.FromDoubleField("popularity")
        .Add(DoubleValuesSource.Constant(1));

var ranked = new FunctionScoreQuery(
    new TermQuery("body", "search"),
    popularity,
    ScoreMode.Multiply);
```

`FunctionQuery` matches all live documents and ranks them directly from a
source. Custom sources can calculate freshness, distance or application
signals by deriving from `DoubleValuesSource`.

## Related query surfaces

- `SpanFirstQuery`, `SpanContainingQuery`, `SpanWithinQuery`,
  `FieldMaskingSpanQuery` and `SpanMultiTermQueryWrapper` compose positional
  matching.
- `AnalysingQueryParser` analyses literal portions of wildcard and prefix
  terms.
- `ComplexPhraseQueryParser` turns multi-term and alternative clauses inside
  quotes into span queries.
- `TermsQuery` accepts exact UTF-8 terms for large sets without converting
  them to strings during lookup.
