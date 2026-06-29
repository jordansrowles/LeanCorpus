# Token filters

Token filters run after tokenisation and before stemming. They rewrite, drop, or add tokens.

## Build a pipeline

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

## Filters by role

| Role | Filters |
|---|---|
| Normalisation | `LowercaseFilter`, `AccentFoldingFilter`, `DecimalDigitFilter`, `ReverseStringFilter`, `WordDelimiterFilter`, `TruncateTokenFilter` |
| Selection | `StopWordFilter`, `LengthFilter`, `UniqueTokenFilter`, `KeepWordFilter`, `TypeTokenFilter`, `LimitTokenCountFilter` |
| Stemming | `PorterStemmerFilter`, `HunspellStemFilter`, `KeywordMarkerFilter` |
| Synonyms | `SynonymGraphFilter`, `FlattenGraphFilter`, `ShingleFilter` |
| Language | `ElisionFilter` |
| Phonetics | `MetaphoneFilter`, `PhoneticAlternatesFilter` |

`KeywordMarkerFilter` marks tokens that compatible stemmers should skip.

## Char filters

Char filters mutate the input before tokenisation:

- `HtmlStripCharFilter`
- `MappingCharFilter`
- `PatternReplaceCharFilter`

Attach them through `IndexWriterConfig.CharFilters`.

## Order matters

Lowercase before stop words. Stemming after both. Synonym expansion after normalisation.

## See also

- [Analysis overview](index.md)
- [Tokenisers](02-tokenisers.md)
- [Stemmers](04-stemmers.md)
- <xref:Rowles.LeanCorpus.Analysis.Analysers.Analyser>
