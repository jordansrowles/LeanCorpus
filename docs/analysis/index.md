# Analysis overview

Analysis turns raw text into the terms LeanCorpus stores and queries. Use the same pipeline at index-time and query-time so terms line up.

## The parts

| Component | Role | Starting point |
|---|---|---|
| `IAnalyser` | End-to-end pipeline | `StandardAnalyser`, `StemmedAnalyser`, `LanguageAnalyser`, `IcuAnalyser` |
| `ITokeniser` | Splits input into tokens | `Tokeniser`, `Uax29UrlEmailTokeniser`, `IcuTokeniser` |
| `ITokenFilter` | Rewrites or drops tokens | `LowercaseFilter`, `StopWordFilter`, `SynonymGraphFilter` |
| `ICharFilter` | Rewrites input text before tokenisation | `HtmlStripCharFilter`, `MappingCharFilter`, `PatternReplaceCharFilter` |
| `IStemmer` | Reduces tokens to root forms | `EnglishStemmer`, `FrenchStemmer`, `GermanStemmer` |

## What to start with

- `StandardAnalyser` for general text with lowercase and stop-word removal.
- `StemmedAnalyser` for English text where broader recall matters.
- `AnalyserFactory.Create("en")` (or another language code) for a built-in language pipeline.
- `IcuAnalyser` or `IcuTokeniser` when Unicode segmentation matters.
- `Analyser` directly for a custom tokeniser and filter chain.

## English stemming choices

| Type | Behaviour | When |
|---|---|---|
| `EnglishStemmer` | Porter-based | Default for English. Used by `AnalyserFactory.Create("en")`. |
| `LightEnglishStemmer` | Lighter suffix stripping | When Porter is too aggressive. |
| `KStemmer` | Lexicon-validated (Krovetz-inspired) | When false stems cost more than missed stems. Needs `KStemLexicon.FromFile`. |

`StemmedAnalyser` also uses Porter stemming.

## Custom pipeline

```csharp
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

var analyser = new Analyser(
    tokeniser: new Uax29UrlEmailTokeniser(),
    new LowercaseFilter(),
    new StopWordFilter(StopWords.English),
    new SynonymGraphFilter(new SynonymMap(new Dictionary<string, string[]>
    {
        ["tv"] = ["television"]
    })));
```

## See also

- [Analysers](01-analysers.md)
- [Tokenisers](02-tokenisers.md)
- [Token filters](03-token-filters.md)
- [Stemmers](04-stemmers.md)
- [Stop words and token budget](05-stop-words-and-token-budget.md)
- [Lexicons](06-lexicons.md)
