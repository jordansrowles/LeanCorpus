# Boolean queries

`BooleanQuery` combines clauses using <xml>Occur</xml>:

| Occur | Description |
|---|---|
| `Must` | Required. |
| `Should` | Optional. Increases relevance. |
| `MustNot` | Exclude. |

## Direct construction

```csharp
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;

var query = new BooleanQuery.Builder()
    .Add(new TermQuery("title", "fox"),  Occur.Must)
    .Add(new TermQuery("title", "quick"), Occur.Should)
    .Add(new TermQuery("title", "lazy"),  Occur.MustNot)
    .Build();
```

## Fluent builder

```csharp
var query = new BooleanQueryBuilder()
    .Must(new TermQuery("title", "fox"))
    .Should(new TermQuery("title", "quick"))
    .MustNot(new TermQuery("title", "lazy"))
    .Build();
```

## Pure-filter mode

A `BooleanQuery` containing only `MustNot` clauses matches everything that satisfies
the exclusions. Wrap in `ConstantScoreQuery` to skip BM25 when scoring is irrelevant.

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.BooleanQuery>
- <xref:Rowles.LeanCorpus.Search.Parsing.BooleanQueryBuilder>
- <xref:Rowles.LeanCorpus.Search.Occur>
