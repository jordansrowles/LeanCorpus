# Analysis overview

Analysis turns raw text into the terms that LeanCorpus stores and queries. In most
applications, the same analysis pipeline should run at index-time and query-time so
that the stored terms and query terms line up.

## The moving parts

| Component | Role | Typical starting point |
|---|---|---|
| `IAnalyser` | End-to-end pipeline | `StandardAnalyser`, `StemmedAnalyser`, `LanguageAnalyser`, `IcuAnalyser` |
| `ITokeniser` | Splits input into tokens | `Tokeniser`, `Uax29UrlEmailTokeniser`, `IcuTokeniser` |
| `ITokenFilter` | Rewrites or drops tokens | `LowercaseFilter`, `StopWordFilter`, `SynonymGraphFilter` |
| `ICharFilter` | Rewrites the input text before tokenisation | `HtmlStripCharFilter`, `MappingCharFilter`, `PatternReplaceCharFilter` |
| `IStemmer` | Reduces tokens to broader term roots | `EnglishStemmer`, `FrenchStemmer`, `GermanStemmer` |

## What to start with

- Use `StandardAnalyser` when lowercase normalisation and stop-word removal are enough.
- Use `StemmedAnalyser` when you want the standard pipeline plus Porter-based English stemming.
- Use `AnalyserFactory.Create("en")` or another language code when you want a built-in language pipeline.
- Use `IcuAnalyser` or `IcuTokeniser` when Unicode-heavy text needs better segmentation than the basic tokeniser.
- Use `Analyser` directly when you need a custom tokeniser and filter chain.

## English stemming choices

LeanCorpus currently exposes more than one public English stemmer. They are not interchangeable.

| Type | Behaviour | When to use it |
|---|---|---|
| `EnglishStemmer` | Porter-based English stemming | Default choice for most English full-text search. This is the English stemmer used by `AnalyserFactory.Create("en")`. |
| `LightEnglishStemmer` | Lighter suffix stripping with a smaller rule set | Use only when you specifically want less aggressive stemming. |
| `KStemmer` | Lexicon-validated English stemming inspired by Krovetz | Uses a lexicon loaded from disk via `KStemLexicon.FromFile`, with optional `IKStemLexicon` override support. |

`StemmedAnalyser` also uses Porter stemming. If you want one default English choice for
consumer-facing documentation, treat `EnglishStemmer` as that default and reach for
`LightEnglishStemmer` or `KStemmer` only deliberately.

## Build a pipeline

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
