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
| `JapaneseTokeniser` | Dictionary-backed least-cost segmentation using the versioned Japanese `.jlc` codec. Custom dictionaries are owned by `JapaneseDictionary` and borrowed by the tokeniser |
| `PathTreeTokeniser` | Path hierarchy tokeniser: compound tokens from root to leaf (or leaf to root in suffix mode). Root-aware parsing for drive letters, UNC paths, and scheme URIs |
| `IcuTokeniser` | Unicode-aware segmentation. Thai opt-in via constructor |
| `UrlEmailTokeniser` | Preserves URLs, emails, hashtags, and mentions using Unicode-aware word heuristics; it does not claim UAX #29 conformance. Thai opt-in |
| `ThaiTokeniser` | Thai segmentation with dictionary. Needs a lexicon loaded from file or stream |
| `PatternTokeniser` | Regex-based tokenisation. Accepts a pattern string and optional group index |
| `MediaWikiTokeniser` | MediaWiki markup: headings, links, categories, citations |

Thai delegation in `IcuTokeniser` and `UrlEmailTokeniser` preserves the nested tokeniser's position increments, graph position lengths, types and payloads. Only character offsets are shifted back to the original input.

## Picking one

- `Tokeniser` for ordinary mixed-alphanumeric text.
- `IcuTokeniser` or `IcuAnalyser` when Unicode word boundaries matter.
- `UrlEmailTokeniser` for social, web, or support text.
- `PathTreeTokeniser` for indexing filesystem paths. Use forward mode for directory hierarchies, suffix mode for IDE-style file search.

  - With depth payloads: `new PathTreeTokeniser { EmitDepthPayloads = true }` attaches depth metadata for shallow-match boosting.
  - Suffix mode: `new PathTreeTokeniser { SuffixMode = true }` emits leaf-to-root tokens like `["user.cs", "models/user.cs", ...]`.
- Use `JapaneseTokeniser` for dictionary-backed Japanese segmentation. The default `.jlc` dictionary is shared for the process lifetime.

### Japanese dictionary lifetime

Own a custom dictionary for at least as long as its tokenisers and analysers:

```csharp
using var dictionary = new JapaneseDictionary(dictionaryPath);
var tokeniser = new JapaneseTokeniser(dictionary);
var analyser = new LanguageAnalyser(tokeniser, StopWords.Japanese, stemmer: null);
```

`JapaneseTokeniser` and its thread-local copies borrow the dictionary. Disposing
the dictionary waits for active tokenisation calls; later calls through those
tokenisers throw `ObjectDisposedException`. For a custom dictionary analyser
with an explicit owner in one value, use
`AnalyserFactory.CreateOwnedJapaneseAnalyser(dictionaryPath)` and dispose the
returned analyser after its calls and thread-local analysers finish.
## Custom pipeline

```csharp
var analyser = new Analyser(
    tokeniser: new UrlEmailTokeniser(),
    new LowercaseFilter(),
    new TypeTokenFilter([
        UrlEmailTokeniser.UrlType,
        UrlEmailTokeniser.EmailType
    ]));
```

## See also

- [Analysis overview](index.md)
- [Analysers](01-analysers.md)
- [Token filters](03-token-filters.md)
- <xref:Rowles.LeanCorpus.Analysis.Tokenisers.ISpanTokeniser>

NGram and EdgeNGram lengths count Unicode scalars. Their sink and streaming APIs
share the same boundary generator; offsets remain UTF-16 source indices. Each
isolated surrogate consumes one invalid unit, while valid pairs remain intact.
