# Boolean queries

`BooleanQuery` combines clauses with `Occur`:

| Occur | Description |
|---|---|
| `Must` | Required |
| `Should` | Optional; increases relevance |
| `MustNot` | Exclude |

## Builder

```csharp
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

## Pure filter mode

A `BooleanQuery` with only `MustNot` clauses matches everything that passes the exclusions. Wrap in `ConstantScoreQuery` to skip BM25:

```csharp
var filter = new ConstantScoreQuery(
    new BooleanQuery.Builder()
        .Add(new TermQuery("status", "draft"), Occur.MustNot)
        .Build(),
    score: 0f);
```

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.BooleanQuery>
- <xref:Rowles.LeanCorpus.Search.Parsing.BooleanQueryBuilder>
