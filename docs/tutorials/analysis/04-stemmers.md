# Stemmers

Stemmers reduce related word forms to a broader shared term. This usually improves
recall, but it can also make matching less precise.

## English stemmers

| Type | Behaviour | Notes |
|---|---|---|
| `EnglishStemmer` | Porter-based English stemming | Default choice for most English search. `AnalyserFactory.Create("en")` uses this. |
| `LightEnglishStemmer` | Lighter suffix stripping | Less aggressive than Porter. Use it only when you want that trade-off explicitly. |
| `KStemmer` | Lexicon-validated English stemming inspired by Krovetz | Requires a `KStemLexicon` loaded via `KStemLexicon.FromFile`. Does not embed a default lexicon. |

| `HunspellStemmer` | Hunspell dictionary-based stemming | Requires a `HunspellDictionary` loaded from `.aff`/`.dic` files. |

`StemmedAnalyser` also applies Porter stemming through `PorterStemmerFilter`, so it
lines up more closely with `EnglishStemmer` than with `LightEnglishStemmer`.

## Hunspell stemming

`HunspellStemmer` uses Hunspell dictionaries for morphological stemming. It covers
many languages and handles irregular forms better than algorithmic stemmers:

```csharp
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Store;

var dict = HunspellDictionary.FromFiles(
    new MMapDirectory("/dictionaries/en_US"));

var stemmer = new HunspellStemmer(dict);

var analyser = new StemmerAnalyser(
    tokeniser: new Tokeniser(),
    stemmer: stemmer);
```

Alternatively, use `HunspellStemFilter` in a custom pipeline to keep the stemmer
separate from the tokeniser chain.

## Other built-in stemmers

LeanCorpus also ships `ArabicStemmer`, `ChineseStemmer`, `DutchStemmer`,
`FrenchStemmer`, `GermanStemmer`, `ItalianStemmer`, `JapaneseStemmer`,
`KoreanStemmer`, `PortugueseStemmer`, `RussianStemmer`, and `SpanishStemmer`.

These are exposed as `IStemmer` implementations for use with `LanguageAnalyser` or
your own custom analyser pipeline.

## Choosing one

- Start with `EnglishStemmer` for ordinary English full-text search.
- Use `LightEnglishStemmer` only when Porter stemming is clearly too aggressive for your corpus.
- Use `KStemmer` when false stems are more costly than missed stems. Provide a `KStemLexicon` via `KStemLexicon.FromFile`.
- Use `LanguageAnalyser` when you want stop words and stemming packaged together for a supported language.
- Skip stemming entirely when exact forms matter more than recall.

## Example

```csharp
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

var analyser = new LanguageAnalyser(
    tokeniser: new Tokeniser(),
    stopWords: StopWords.English,
    stemmer: new EnglishStemmer());
```

## See also

- [Analysis overview](index.md)
- [Analysers](01-analysers.md)
- <xref:Rowles.LeanCorpus.Analysis.Stemmers.IStemmer>
