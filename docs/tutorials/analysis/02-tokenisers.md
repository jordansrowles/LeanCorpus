# Tokenisers

Tokenisers split raw text into token boundaries. Choose based on the input structure.

| Type | Notes |
|---|---|
| `Tokeniser` | Default. Splits on punctuation and whitespace; keeps letters and digits together |
| `WhitespaceTokeniser` | Splits on whitespace only |
| `KeywordTokeniser` | Emits the whole input as one token |
| `LetterTokeniser` | Emits letter runs only; drops digits and punctuation |
| `NGramTokeniser` | Sliding n-grams across tokens |
| `EdgeNGramTokeniser` | Prefix n-grams; useful for autocomplete-style matching |
| `CJKBigramTokeniser` | Bigrams for CJK text |
| `IcuTokeniser` | Unicode-aware segmentation. Thai opt-in via constructor |
| `Uax29UrlEmailTokeniser` | Preserves URLs, emails, hashtags, and mentions as single tokens. Thai opt-in |
| `ThaiTokeniser` | Thai segmentation with dictionary. Needs a lexicon loaded from file or stream |
| `MediaWikiTokeniser` | MediaWiki markup: headings, links, categories, citations |

## Picking one

- `Tokeniser` for ordinary mixed-alphanumeric text.
- `IcuTokeniser` or `IcuAnalyser` when Unicode word boundaries matter.
- `Uax29UrlEmailTokeniser` for social, web, or support text.
- `KeywordTokeniser` for identifiers or whole-field exact matching.
- Inject a `ThaiTokeniser` for Thai: `new IcuTokeniser(ThaiTokeniser.FromFile("lexicons/thai-dict.txt"))`.

## Custom pipeline

```csharp
var analyser = new Analyser(
    tokeniser: new Uax29UrlEmailTokeniser(),
    new LowercaseFilter(),
    new TypeTokenFilter([
        Uax29UrlEmailTokeniser.UrlType,
        Uax29UrlEmailTokeniser.EmailType
    ]));
```

## See also

- [Analysis overview](index.md)
- [Analysers](01-analysers.md)
- [Token filters](03-token-filters.md)
- <xref:Rowles.LeanCorpus.Analysis.Tokenisers.ITokeniser>
