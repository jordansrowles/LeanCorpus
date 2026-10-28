# More like this

`MoreLikeThisQuery` finds documents similar to a source document by extracting representative terms from its term vectors and turning them into a weighted boolean query.

```csharp
var mlt = new MoreLikeThisQuery(
    docId: 42,
    fields: new[] { "title", "body" },
    parameters: new MoreLikeThisParameters
    {
        MinTermFreq    = 2,
        MinDocFreq     = 5,
        MaxQueryTerms  = 25,
        MinWordLength  = 3,
        BoostByScore   = true
    });

var hits = searcher.Search(mlt, topN: 10);
```

## Parameters

| Parameter | Default | Effect |
|---|---|---|
| `MinTermFreq` | `1` | Minimum occurrences in the source doc |
| `MinDocFreq` | `1` | Minimum docs containing the term |
| `MaxDocFreq` | `int.MaxValue` | Cap on doc frequency for common terms |
| `MaxQueryTerms` | `25` | Maximum terms in the generated query |
| `MinWordLength` | `3` | Minimum term length |
| `BoostByScore` | `true` | Weight terms by their TF-IDF score |

Raise `MinDocFreq` to filter rare terms that dominate similarity. Lower `MaxDocFreq` to filter ultra-common terms.

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.MoreLikeThisQuery>
- <xref:Rowles.LeanCorpus.Search.Queries.MoreLikeThisParameters>
