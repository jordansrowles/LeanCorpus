# Stemmers

Stemmers reduce related word forms to a shared root. Improves recall; can reduce precision.

## English stemmers

| Type | Behaviour | Notes |
|---|---|---|
| `EnglishStemmer` | Porter-based | Default for English. `AnalyserFactory.Create("en")` uses it |
| `LightEnglishStemmer` | Lighter suffix stripping | Less aggressive than Porter |
| `KStemmer` | Lexicon-validated (Krovetz-inspired) | Needs `KStemLexicon.FromFile` |
| `HunspellStemmer` | Hunspell dictionary-based | Needs `.aff`/`.dic` files. Handles irregular forms |

`StemmedAnalyser` also applies Porter stemming via `PorterStemmerFilter`.

## Hunspell stemming

```csharp
using Rowles.LeanCorpus.Analysis.Stemmers;

var dict = HunspellDictionary.FromFiles(new MMapDirectory("/dictionaries/en_US"));
var stemmer = new HunspellStemmer(dict);

var analyser = new StemmerAnalyser(
    tokeniser: new Tokeniser(),
    stemmer: stemmer);
```

## Other languages

`ArabicStemmer`, `ChineseStemmer`, `DutchStemmer`, `FrenchStemmer`, `GermanStemmer`, `ItalianStemmer`, `JapaneseStemmer`, `KoreanStemmer`, `PortugueseStemmer`, `RussianStemmer`, `SpanishStemmer`.

Use with `LanguageAnalyser` or a custom pipeline.

## Choosing

- `EnglishStemmer` for ordinary English search.
- `LightEnglishStemmer` when Porter is too aggressive.
- `KStemmer` when false stems are worse than missed stems.
- `LanguageAnalyser` for packaged stop words + stemming for a supported language.
- Skip stemming when exact forms matter more than recall.

## Example

```csharp
var analyser = new LanguageAnalyser(
    tokeniser: new Tokeniser(),
    stopWords: StopWords.English,
    stemmer: new EnglishStemmer());
```

## See also

- [Analysis overview](index.md)
- [Analysers](01-analysers.md)
- <xref:Rowles.LeanCorpus.Analysis.Stemmers.IStemmer>
