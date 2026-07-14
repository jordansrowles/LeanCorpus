# Phrase and proximity

## PhraseQuery

Matches documents containing terms in order.

```csharp
var exact = new PhraseQuery("title", "quick", "brown", "fox");
```

`Slop` allows extra positions between terms. Slop `2` matches "quick X Y brown fox" (any order within the window):

```csharp
var loose = new PhraseQuery("title", slop: 2, "quick", "fox");
```

Default slop is `0` (exact).

## SpanNearQuery

For nested proximity use span queries:

```csharp
var near = new SpanNearQuery(
    clauses: new[]
    {
        new SpanTermQuery("body", "machine"),
        new SpanTermQuery("body", "learning")
    },
    slop: 3,
    inOrder: true);
```

Combine with `SpanOrQuery` and `SpanNotQuery` for richer positional logic.

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.PhraseQuery>
- <xref:Rowles.LeanCorpus.Search.Queries.SpanNearQuery>
