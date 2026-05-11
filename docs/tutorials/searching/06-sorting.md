# Sorting

By default, `IndexSearcher.Search` returns hits ordered by relevance score.

## Sort by field at query time

Use a sorted-search overload, supplying one or more `SortField` values:

```csharp
using Rowles.LeanCorpus.Search.Scoring;

var sort = new[]
{
    new SortField("price", SortFieldType.Double, descending: false),
    new SortField("id",    SortFieldType.String, descending: false),
};

var hits = searcher.Search(new TermQuery("category", "books"), 10, sort);
```

Sort field types include `Score`, `String`, `Long`, `Int`, `Double`, `Float`.

## Index-time sort

Configure <xref:Rowles.LeanCorpus.Index.Indexer.IndexSort> on the writer to
physically reorder documents within each segment as they are flushed. Query-time
sorted searches still collect and order matching hits by the requested field.

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
