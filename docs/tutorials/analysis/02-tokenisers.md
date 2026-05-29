# Tokenisers

Tokenisers split raw text into token boundaries before token filters and stemmers run.
Choose them based on the structure of the input, not on the stemming strategy.

## Built-in tokenisers

| Type | Notes |
|---|---|
| `Tokeniser` | Default word tokeniser. Splits on punctuation and whitespace, keeps letters and digits together. |
| `WhitespaceTokeniser` | Splits only on whitespace. |
| `KeywordTokeniser` | Emits the full input as a single token. |
| `LetterTokeniser` | Emits only letter runs, dropping digits and punctuation boundaries. |
| `NGramTokeniser` | Produces sliding n-grams across tokens. |
| `EdgeNGramTokeniser` | Produces prefix n-grams. Useful for prefix-style matching. |
| `CJKBigramTokeniser` | Produces bigrams for CJK text. |
| `IcuTokeniser` | Unicode-aware tokeniser for broader language coverage. Thai segmentation is opt-in via the constructor. |
| `Uax29UrlEmailTokeniser` | Preserves URLs, email addresses, hashtags, and mentions as single tokens. Thai segmentation is opt-in. |
| `ThaiTokeniser` | Segments Thai runs with dictionary support. Requires a lexicon loaded from file or stream. |
| `MediaWikiTokeniser` | Tokenises MediaWiki markup such as headings, links, categories, and citations. |

## Picking one

- Start with `Tokeniser` for ordinary English or mixed alphanumeric text.
- Use `IcuTokeniser` or `IcuAnalyser` when Unicode word boundaries matter.
- Inject a `ThaiTokeniser` for Thai segmentation: `new IcuTokeniser(ThaiTokeniser.FromFile("lexicons/thai-dict.txt"))`.
- Use `Uax29UrlEmailTokeniser` for social, web, or support text where URLs and email addresses should stay intact.
- Use `KeywordTokeniser` for identifiers, tags, or whole-field exact matching.

## Custom pipeline example

```csharp
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

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
