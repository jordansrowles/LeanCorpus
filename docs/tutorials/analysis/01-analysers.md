# Analysers

An analyser is the top-level pipeline: tokenise, normalise, filter, output terms.

## Built-in analysers

| Type | Pipeline | When |
|---|---|---|
| `StandardAnalyser` | Basic tokenise, lowercase, stop words | General-purpose text |
| `StemmedAnalyser` | `StandardAnalyser` + Porter stemming | English text, broader recall |
| `LanguageAnalyser` | Tokenise, lowercase, stop words, optional stemmer | Language-specific with custom tokeniser/stemmer |
| `IcuAnalyser` | `IcuTokeniser`, lowercase, stop words, optional Thai | Unicode-heavy text |
| `WhitespaceAnalyser` | `WhitespaceTokeniser` only | Punctuation and case should stay |
| `KeywordAnalyser` | `KeywordTokeniser` only | Whole field as one token |
| `SimpleAnalyser` | Letter-only tokenise, lowercase | Ignore digits and punctuation |
| `Analyser` | Your tokeniser and filters | Custom pipeline |

## Three starting points

```csharp
using Rowles.LeanCorpus.Analysis;

var standard = new StandardAnalyser();
var stemmed  = new StemmedAnalyser();
var french   = AnalyserFactory.Create("fr");
```

## AnalyserFactory languages

`AnalyserFactory.Create(string)` accepts BCP 47 codes. Region and script subtags are stripped (`en-GB` becomes `en`).

Supported: `en`, `fr`, `de`, `es`, `it`, `pt`, `nl`, `ru`, `ar`, `zh`, `ja`, `ko`.

CJK languages (`zh`, `ja`, `ko`) use bigram tokenisation and skip stemming. `AnalyserFactory.Create("en")` uses `EnglishStemmer`.

## Per-field override

Set the default on `IndexWriterConfig.DefaultAnalyser`. Override per-field by attaching an `IAnalyser` to a `FieldMapping` inside an `IndexSchema`.

## Inspect tokens

```csharp
foreach (var token in standard.Analyse("The Quick Brown Foxes".AsSpan()))
    Console.WriteLine(token.Text);
// quick, brown, foxes
```

## See also

- [Analysis overview](index.md)
- [Tokenisers](02-tokenisers.md)
- [Stemmers](04-stemmers.md)
- <xref:Rowles.LeanCorpus.Analysis.Analysers.AnalyserFactory>
