# Analysers

An analyser is the top-level analysis pipeline. It tokenises input, applies any
normalisation and filtering, and returns the final token stream that indexing and
query execution use.

## Built-in analysers

| Type | Pipeline | Use it when |
|---|---|---|
| `StandardAnalyser` | Basic tokeniser, lowercase normalisation, stop-word removal | You want the default general-purpose analyser. |
| `StemmedAnalyser` | `StandardAnalyser` plus Porter stemming | You want broader English recall with the standard pipeline. |
| `LanguageAnalyser` | Tokeniser, lowercase normalisation, stop words, optional `IStemmer` | You want a language-specific pipeline or a custom tokeniser plus stemmer combination. |
| `IcuAnalyser` | `IcuTokeniser`, lowercase normalisation, stop-word removal, optional extra filters, optional Thai tokeniser | You need better Unicode-aware segmentation. Inject a `ThaiTokeniser` for Thai support. |
| `WhitespaceAnalyser` | `WhitespaceTokeniser` only | Punctuation and case should stay intact. |
| `KeywordAnalyser` | `KeywordTokeniser` only | The whole field should be treated as one token. |
| `SimpleAnalyser` | Letter-only tokenisation and lowercase normalisation | You want a small analyser that ignores digits and punctuation. |
| `Analyser` | Your supplied tokeniser and filters | You want a custom pipeline. |

## Three common starting points

```csharp
using Rowles.LeanCorpus.Analysis;

var standard = new StandardAnalyser();
var stemmed = new StemmedAnalyser();
var french = AnalyserFactory.Create("fr");
```

## `AnalyserFactory` languages

`AnalyserFactory.Create(string)` accepts BCP 47 codes. Region and script subtags are
stripped, so `en-GB` becomes `en` and `zh-Hans` becomes `zh`.

Supported: `en`, `fr`, `de`, `es`, `it`, `pt`, `nl`, `ru`, `ar`, `zh`, `ja`, `ko`.

CJK languages (`zh`, `ja`, `ko`) use bigram tokenisation and skip stemming. The
English pipeline created by `AnalyserFactory` uses `EnglishStemmer`, not
`LightEnglishStemmer`.

## Per-field selection

Set the writer-wide default through `IndexWriterConfig.DefaultAnalyser`. To override
per-field, attach an `IAnalyser` to a <xref:Rowles.LeanCorpus.Index.Indexer.FieldMapping>
inside an <xref:Rowles.LeanCorpus.Index.Indexer.IndexSchema>.

## Inspecting tokens

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
- <xref:Rowles.LeanCorpus.Analysis.Analysers.IAnalyser>
