# Spelling suggestions

`DidYouMeanSuggester` returns alternative spellings, ranked by document frequency divided by edit distance.

```csharp
using Rowles.LeanCorpus.Search.Suggestions;

var suggestions = DidYouMeanSuggester.Suggest(
    searcher, field: "title", queryTerm: "lukcy",
    maxEdits: 2, topN: 5);

foreach (var s in suggestions)
    Console.WriteLine($"{s.Term} (distance={s.Distance}, df={s.DocFreq})");
```

## Reusing the spell index

For repeated suggestions on the same field, build the index once:

```csharp
var spell = SpellIndex.Build(searcher, "title");
var s1 = DidYouMeanSuggester.Suggest(spell, "lukcy", maxEdits: 2, topN: 5);
var s2 = DidYouMeanSuggester.Suggest(spell, "frmo",  maxEdits: 2, topN: 5);
```

## Tuning

| Parameter | Guidance |
|---|---|
| `maxEdits` | Levenshtein cap; 1–2 |
| `topN` | Number of suggestions |

## See also

- <xref:Rowles.LeanCorpus.Search.Suggestions.DidYouMeanSuggester>
- <xref:Rowles.LeanCorpus.Search.Suggestions.SpellIndex>
