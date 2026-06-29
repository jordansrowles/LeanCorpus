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

## See also

- <xref:Rowles.LeanCorpus.Search.Scoring.SortField>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexSort>
