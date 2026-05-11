# More like this

`MoreLikeThisQuery` finds documents similar to a source document by extracting
representative terms from its term vectors and turning them into a weighted boolean
query.

## Use

```csharp
using Rowles.LeanCorpus.Search.Queries;

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

## Parameter defaults

| Parameter | Default |
|---|---|
| `MinTermFreq` | `1` |
| `MinDocFreq` | `1` |
| `MaxDocFreq` | `int.MaxValue` |
| `MaxQueryTerms` | `25` |
| `MinWordLength` | `3` |
| `BoostByScore` | `true` |

Raising `MinDocFreq` filters out terms that appear in only a handful of documents
and would otherwise dominate similarity. Lowering `MaxDocFreq` filters ultra-common
terms.

## See also

- <xref:Rowles.LeanCorpus.Search.Queries.MoreLikeThisQuery>
- <xref:Rowles.LeanCorpus.Search.Queries.MoreLikeThisParameters>
