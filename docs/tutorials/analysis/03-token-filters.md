# Token filters

Token filters run after tokenisation. They can rewrite token text, drop tokens, add
derived tokens, or constrain the stream before indexing.

## Build a custom pipeline

```csharp
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Analysis.Filters;

var analyser = new Analyser(
    tokeniser: new Tokeniser(),
    new LowercaseFilter(),
    new StopWordFilter(StopWords.English),
    new PorterStemmerFilter());
```

## Token filters by role

| Role | Filters |
|---|---|
| Normalisation | `LowercaseFilter`, `AccentFoldingFilter`, `DecimalDigitFilter`, `ReverseStringFilter`, `WordDelimiterFilter`, `TruncateTokenFilter` |
| Selection and limits | `StopWordFilter`, `LengthFilter`, `UniqueTokenFilter`, `KeepWordFilter`, `TypeTokenFilter`, `LimitTokenCountFilter` |
| Stemming and morphology | `PorterStemmerFilter`, `HunspellStemFilter`, `KeywordMarkerFilter` |
| Synonyms and graphs | `SynonymGraphFilter`, `FlattenGraphFilter`, `ShingleFilter` |
| Language and text cleanup | `ElisionFilter` |
| Phonetics | `MetaphoneFilter`, `PhoneticAlternatesFilter` |

`KeywordMarkerFilter` is not a text-rewriting filter on its own. It marks tokens that
compatible analysers should leave untouched during stemming.

## Char filters

Char filters mutate the input text before tokenisation. LeanCorpus ships:

- `HtmlStripCharFilter`
- `MappingCharFilter`
- `PatternReplaceCharFilter`

Attach them through `IndexWriterConfig.CharFilters`.

## Order matters

Filters run left to right. Lowercase before stop words. Apply stemming after both.
Place synonym expansion after the normalisation steps it depends on.

## See also

- [Analysis overview](index.md)
- [Tokenisers](02-tokenisers.md)
- [Stemmers](04-stemmers.md)
- <xref:Rowles.LeanCorpus.Analysis.Analysers.Analyser>
- <xref:Rowles.LeanCorpus.Analysis.ITokenFilter>
