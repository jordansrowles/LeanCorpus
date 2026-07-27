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
| `CJKBigramTokeniser` | Overlapping bigrams for CJK ideographs with supplementary-plane support |
| `ChineseLexiconTokeniser` | Greedy longest-match Chinese segmentation with unigram fallback |
| `JapaneseTokeniser` | Character-class-based segmentation using Kuromoji `CharacterDefinition.dat`. Splits at script boundaries (kanji, hiragana, katakana) |
| `PathTreeTokeniser` | Path hierarchy tokeniser: compound tokens from root to leaf (or leaf to root in suffix mode). Root-aware parsing for drive letters, UNC paths, and scheme URIs |
| `IcuTokeniser` | Unicode-aware segmentation. Thai opt-in via constructor |
| `Uax29UrlEmailTokeniser` | Preserves URLs, emails, hashtags, and mentions as single tokens. Thai opt-in |
| `ThaiTokeniser` | Thai segmentation with dictionary. Needs a lexicon loaded from file or stream |
| `PatternTokeniser` | Regex-based tokenisation. Accepts a pattern string and optional group index |
| `MediaWikiTokeniser` | MediaWiki markup: headings, links, categories, citations |

## Picking one

- `Tokeniser` for ordinary mixed-alphanumeric text.
- `IcuTokeniser` or `IcuAnalyser` when Unicode word boundaries matter.
- `Uax29UrlEmailTokeniser` for social, web, or support text.
- `PathTreeTokeniser` for indexing filesystem paths. Use forward mode for directory hierarchies, suffix mode for IDE-style file search.

  - With depth payloads: `new PathTreeTokeniser { EmitDepthPayloads = true }` attaches depth metadata for shallow-match boosting.
  - Suffix mode: `new PathTreeTokeniser { SuffixMode = true }` emits leaf-to-root tokens like `["user.cs", "models/user.cs", ...]`.
- Use `JapaneseTokeniser` with Kuromoji `CharacterDefinition.dat` in `lexicons/kuromoji/` for Japanese script-boundary segmentation.
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
