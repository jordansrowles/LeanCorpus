# Field collapsing

Field collapsing returns only the best document per unique value of a field. One result per category, author, or product.

```csharp
using Rowles.LeanCorpus.Search.Scoring;

var collapse = new CollapseField("category", CollapseMode.TopScore);
var hits = searcher.SearchWithCollapse(
    new TermQuery("body", "laptop"), topN: 10, collapse);
```

The collapse field must be backed by `SortedDocValues`. `StringField` and `TextField` both populate sorted doc values by default.

## Collapse modes

| Mode | Behaviour |
|---|---|
| `TopScore` (default) | Highest-scoring document per group |
| `MinScore` | Lowest-scoring document per group |

Collects matching documents during the search pass with a side-collector, reads the collapse field value per match, and keeps the best-scoring document per unique value. The final top-N is drawn from the survivors, still ordered by score.

## Collapsing vs faceting

Faceting counts documents per value. Collapsing picks one document per value. Combine both by running a facet request separately.

## See also

- <xref:Rowles.LeanCorpus.Search.Scoring.CollapseField>
- <xref:Rowles.LeanCorpus.Search.Searcher.IndexSearcher.SearchWithCollapse%2A>
