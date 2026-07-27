# Spelling suggestions

`DidYouMeanSuggester` proposes indexed terms close to a misspelt query term. Suggestions are ranked by:

```text
document frequency / (1 + edit distance)
```

This favours common terms while still penalising larger edits.

## Suggest from a searcher

```csharp
using Rowles.LeanCorpus.Search.Suggestions;

var suggestions = DidYouMeanSuggester.Suggest(
    searcher,
    field: "title",
    queryTerm: "lukcy",
    maxEdits: 2,
    topN: 5);

foreach (var suggestion in suggestions)
{
    Console.WriteLine(
        $"{suggestion.Term} " +
        $"(distance={suggestion.Distance}, df={suggestion.DocFreq})");
}
```

The first call builds and caches a `SpellIndex` for the searcher and field. The cache is tied weakly to the searcher lifetime.

## Reuse explicitly

Build once when an application controls refresh and suggestion lifecycle:

```csharp
var spell = SpellIndex.Build(searcher, "title");

var first = DidYouMeanSuggester.Suggest(
    spell, "lukcy", maxEdits: 2, topN: 5);
var second = DidYouMeanSuggester.Suggest(
    spell, "frmo", maxEdits: 2, topN: 5);

Console.WriteLine($"{spell.TermCount} indexed terms");
```

`SpellIndex.Build` aggregates unique terms and document frequencies across segments. Building costs roughly the unique-term count multiplied by average term length, so rebuild it only after a searcher refresh whose new terms matter to suggestions.

## Candidate algorithm

The index maps character trigrams to term ordinals. At query time it:

1. extracts distinct query trigrams;
2. uses trigram overlap to reject unlikely terms;
3. rejects candidates whose length differs by more than `maxEdits`;
4. calculates bounded Levenshtein distance;
5. ranks surviving terms by frequency and distance.

Queries shorter than three characters have no trigrams, so they fall back to scanning terms that pass the length check.

## Tuning

| Parameter | Guidance |
|---|---|
| `maxEdits` | Usually `1` or `2`. Larger values broaden candidates sharply and are not supported as a general fuzzy-language model. |
| `topN` | Return only enough alternatives for the interface. |
| Field | Use a field whose indexed terms match what users are expected to type. |

Document frequency makes suggestions corpus-specific. A rare correct brand name may rank below a common unrelated term at the same edit distance. Consider application allow-lists, exact-known-term checks, or business weighting before displaying a replacement.

## UI pattern

Do not silently replace the query by default. A safer flow is:

1. run the original query;
2. if results are weak or empty, request suggestions for analysed query terms;
3. display “Did you mean?” alternatives;
4. rerun only when the user chooses one, or clearly label an automatic fallback.

For multi-word input, analyse the query first and correct individual terms. Preserve quoted phrases and operators when reconstructing parser input.

## Language limitations

Character trigrams and Levenshtein distance are most predictable for alphabetic terms. They do not model keyboard layout, phonetics, word segmentation, or language morphology.

CJK behaviour depends heavily on tokenisation. A spelling index built from individual ideographs or short n-grams can produce a large, low-signal candidate set. Test the chosen [tokeniser](../analysis/02-tokenisers.md) and consider a language-specific suggestion service when character edits are not a useful error model.
