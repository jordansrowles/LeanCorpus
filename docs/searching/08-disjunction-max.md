# Disjunction max queries

`DisjunctionMaxQuery` scores a document as `max(sub-query scores) + tieBreakerMultiplier * sum(rest)`. Use when several alternatives match the same concept and you want the best match to dominate.

## Direct construction

```csharp
var query = new DisjunctionMaxQuery(tieBreakerMultiplier: 0.1f)
    .Add(new TermQuery("title", "phone"))
    .Add(new TermQuery("body", "phone"))
    .Freeze();

var hits = searcher.Search(query, topN: 10);
```

## Builder

```csharp
var query = new DisjunctionMaxQuery.Builder()
    .WithTieBreakerMultiplier(0.2f)
    .Add(new TermQuery("title", "laptop"))
    .Add(new TermQuery("description", "laptop"))
    .Add(new TermQuery("specs", "laptop"))
    .Build();
```

## Tie-breaker multiplier

| Multiplier | Behaviour |
|---|---|
| `0` (default) | Only the best clause counts |
| `0.1` | Best dominates; others add 10% |
| `1.0` | Equivalent to boolean `Should` with additive scoring |

## DisMax vs boolean

```csharp
// DisMax: best field wins, others nudge
var dismax = new DisjunctionMaxQuery(0.1f)
    .Add(new TermQuery("title", "apple"))
    .Add(new TermQuery("body", "apple"))
    .Freeze();

// Boolean Should: contributions stack
var boolean = new BooleanQuery.Builder()
    .Add(new TermQuery("title", "apple") { Boost = 3 }, Occur.Should)
    .Add(new TermQuery("body", "apple")  { Boost = 1 }, Occur.Should)
    .Build();
```

## See also

- [Query types](01-query-types.md)
- [Boolean queries](02-boolean-queries.md)
- <xref:Rowles.LeanCorpus.Search.Queries.DisjunctionMaxQuery>
