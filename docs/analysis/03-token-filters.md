# Token filters

Token filters receive tokens after tokenisation and can normalise, remove, replace, or add alternatives. Their order changes indexed terms, positions, offsets, and therefore query behaviour.

## Build a pipeline

```csharp
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Tokenisers;

var analyser = new Analyser(
    tokeniser: new Tokeniser(),
    new LowercaseFilter(),
    new AccentFoldingFilter(),
    new StopWordFilter(StopWords.English),
    new PorterStemmerFilter());
```

The example normalises before checking stop words and stems only the surviving terms.

## Normalisation and rewriting

| Filter | Behaviour and configuration |
|---|---|
| `LowercaseFilter` | Lowercases token text. Place before case-sensitive dictionaries. |
| `AccentFoldingFilter` | Folds accented Latin characters to simpler forms. Decide whether the application needs originals as well. |
| `DecimalDigitFilter` | Converts Unicode decimal digits to their ASCII equivalents. |
| `ClassicFilter` | Removes English possessives and periods from uppercase acronyms such as `U.S.A.`. |
| `PatternReplaceFilter` | Applies a regular expression replacement to each token. Accepts a pattern and replacement, or a compiled `Regex`. |
| `ReverseStringFilter` | Reverses token text, useful for suffix-oriented indexing. Query analysis must mirror it. |
| `TruncateTokenFilter` | Limits each token to `maxLength`. Truncation can create collisions. |
| `HyphenatedWordsFilter` | Recombines words split across hyphenated line endings. Configure the separator and use `Finish` by completing the analyser normally. |
| `WordDelimiterFilter` | Splits punctuation, case, and letter-digit transitions. Controls include generated word and number parts, concatenation, original preservation, case splitting, numeric splitting, and possessive stemming. |

Example compound-word configuration:

```csharp
var delimiter = new WordDelimiterFilter
{
    GenerateWordParts = true,
    GenerateNumberParts = true,
    CatenateWords = true,
    SplitOnCaseChange = true,
    SplitOnNumerics = true,
    PreserveOriginal = true,
};
```

For `WiFi4Schools_test`, this can emit component terms and same-position alternatives. Inspect phrase behaviour when enabling several output forms.

## Selection and limits

| Filter | Behaviour and configuration |
|---|---|
| `StopWordFilter` | Removes the default or supplied stop-word set. |
| `LengthFilter` | Keeps tokens between `minLength` and `maxLength`. |
| `KeepWordFilter` | Keeps only tokens in the supplied set. |
| `TypeTokenFilter` | Keeps or rejects configured token types through `keepMatching`. |
| `UniqueTokenFilter` | Removes duplicate token text from one analysed stream. |
| `LimitTokenCountFilter` | Emits at most `maxTokenCount` tokens. Prefer the writer token budget when the requirement is a document-wide safety policy. |

Removing tokens must preserve meaningful position increments. Test phrase queries after adding a selection filter.

## Stemming and protected terms

| Filter | Behaviour and configuration |
|---|---|
| `PorterStemmerFilter` | Applies Porter stemming. Can share a `KeywordMarkerFilter`. |
| `StemTokenFilter` | Wraps another `ISpanStemmer`. Can share a keyword marker. |
| `HunspellStemFilter` | Uses a `HunspellDictionary`; `injectAlternates` controls whether alternatives are emitted. |
| `KeywordMarkerFilter` | Marks supplied terms so compatible stemmers leave them unchanged. |

```csharp
var protectedTerms = new KeywordMarkerFilter(
    ["leancorpus", "dotnet"]);

var analyser = new Analyser(
    new Tokeniser(),
    new LowercaseFilter(),
    protectedTerms,
    new PorterStemmerFilter(protectedTerms));
```

See [Hunspell](07-hunspell.md) for dictionary loading and limitations.

## Synonyms, shingles, and token graphs

| Filter | Behaviour and configuration |
|---|---|
| `SynonymGraphFilter` | Expands source phrases into alternate token-graph edges. |
| `FlattenGraphFilter` | Converts graph edges to unit-length positions for postings. Required before indexing a graph-producing pipeline. |
| `ShingleFilter` | Emits connected token n-gram graph edges. Configure minimum and maximum size, unigram output, and separator. |
| `CommonGramsFilter` | Emits common-word bigrams using a supplied word set and separator. |

```csharp
var synonyms = new SynonymMap();
synonyms.Add("nyc", ["new", "york"]);

var analyser = new Analyser(
    new Tokeniser(),
    new LowercaseFilter(),
    new SynonymGraphFilter(synonyms),
    new FlattenGraphFilter());
```

Expansion increases postings and can change phrase positions. Keep synonym maps bounded and version them with the indexed corpus. Graph-producing filters require `FlattenGraphFilter` at index time; quoted queries retain graph paths and are bounded to prevent unbounded expansion.

## Language and phonetic filters

| Filter | Behaviour and configuration |
|---|---|
| `ElisionFilter` | Removes configured leading articles, with optional case-insensitive matching. Useful for languages with apostrophe elision. |
| `MetaphoneFilter` | Emits a Metaphone encoding. `inject` controls whether the original is retained. |
| `PhoneticAlternatesFilter` | Emits Latin-name phonetic alternatives. Configure `inject` and `maxExpansions`. |

Phonetic expansion is a recall feature, not a replacement for language analysis. Put it on a dedicated field when exact spelling and phonetic matches need different boosts.

## Diagnostics

`CachingTokenFilter` captures materialised `Token` values while forwarding the stream unchanged:

```csharp
var capture = new CachingTokenFilter();
var analyser = new Analyser(
    new Tokeniser(),
    new LowercaseFilter(),
    capture);

capture.Reset();
analyser.Analyse("One TWO", sink);

foreach (var token in capture.Tokens)
    Console.WriteLine($"{token.Text} at {token.StartOffset}");
```

Captured text allocates strings and `Clone()` intentionally returns the same capture instance. Use it for inspection, not as an unnoticed production hot-path filter.

## Character filters

Character filters transform the complete input before tokenisation:

| Filter | Use |
|---|---|
| `HtmlStripCharFilter` | Removes HTML markup |
| `MappingCharFilter` | Applies string mappings |
| `PatternReplaceCharFilter` | Applies a regular expression replacement |

Attach them through `IndexWriterConfig.CharFilters`. Offset-sensitive features need tests because changing source length can affect how offsets relate to original text.

## Ordering checklist

- Apply character filtering before tokenisation.
- Normalise case and accents before lookup filters.
- Mark protected keywords before stemming.
- Expand synonyms after the normalisation expected by the synonym map.
- Flatten graphs before a consumer that requires linear positions.
- Put destructive limits after any expansions they are meant to bound.
- Use equivalent index-time and query-time analysis.
