# Sorting

By default, results are ordered by relevance score.

## Sort by field

```csharp
using Rowles.LeanCorpus.Search.Scoring;

var sort = new[]
{
    new SortField("price", SortFieldType.Double, descending: false),
    new SortField("id",    SortFieldType.String, descending: false),
};

var hits = searcher.Search(new TermQuery("category", "books"), 10, sort);
```

Sort field types: `Score`, `String`, `Long`, `Int`, `Double`, `Float`.

## Index-time sort

`IndexSort` physically reorders documents within each segment at flush time:

```csharp
var config = new IndexWriterConfig
{
    IndexSort = new IndexSort(
        new SortField("publishedAt", SortFieldType.Long, descending: true))
};
```

`SortFieldType.Score` is not allowed for `IndexSort`.

## Early termination

When the search sort matches the index sort (same field, same direction), the searcher walks postings in document-ID order without scoring. Document IDs are already in sort-key order, so the first topN live documents are the correct topN results. No score computation, no key extraction, no heap selection.

## See also

- <xref:Rowles.LeanCorpus.Search.Scoring.SortField>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexSort>
