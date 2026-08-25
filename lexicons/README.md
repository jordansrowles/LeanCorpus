# LeanCorpus lexicons

The `lexicons` directory contains optional language data used by text-analysis components. Some files are human-readable source or runtime data; the Japanese `.jlc` file is a generated binary runtime asset.

## Choose a workflow

| I want to... | Start here |
| --- | --- |
| Use a checked-in lexicon from a repository checkout | [Use a file path](#use-a-file-path) |
| Deploy my own dictionary | [Supply a custom lexicon](#supply-a-custom-lexicon) |
| Package data inside an assembly | [Embed a lexicon](#embed-a-lexicon) |
| Replace or regenerate checked-in data | [Maintain lexicon assets](#maintain-lexicon-assets) |

## Available data

| File | Size or entries | Used by | Provenance and licence |
| --- | ---: | --- | --- |
| `kstem-dict.txt` | About 27,500 entries | `KStemmer` and `KStemLexicon` | Derived from the Lucene.NET KStem word list, Apache 2.0 |
| `thai-dict.txt` | About 200 entries | `ThaiTokeniser` | Minimal starter data; larger ICU data uses the Unicode licence |
| `chinese-dict.txt` | About 2,500 entries | `ChineseLexiconTokeniser` | Derived from CC-CEDICT data, BSD |
| `japanese.jlc` | 325,871 surface forms | `JapaneseTokeniser` | Converted from Lucene.NET Kuromoji data, Apache 2.0 |

> [!IMPORTANT]
> The bundled Thai dictionary is intentionally small and is suitable for examples and tests. Production Thai tokenisation normally needs a larger, workload-appropriate dictionary.

## Use a file path

```csharp
var lexicon = KStemLexicon.FromFile("lexicons/kstem-dict.txt");
var stemmer = new KStemmer(lexicon);

var thai = ThaiTokeniser.FromFile("lexicons/thai-dict.txt");
var analyser = new IcuAnalyser(thaiTokeniser: thai);

using var japanese = new JapaneseTokeniser("lexicons/japanese.jlc");
```

`JapaneseTokeniser` searches parent directories for `lexicons/japanese.jlc` when the default asset is used. A custom codec path is loaded lazily; dispose tokenisers created with a custom path when they are no longer needed.

> [!TIP]
> Resolve deployment paths explicitly. A development checkout layout is not a reliable production content path.

## Supply a custom lexicon

The text lexicons use:

- UTF-8 encoding;
- one entry per line;
- `#` for comments;
- blank-line skipping;
- trimming around each entry.

Use a larger Thai dictionary, a domain-specific Chinese vocabulary or an application-owned KStem source by passing its path or stream to the matching component.

The ICU `thaidict.txt` source can be converted to the one-entry-per-line text format used by `ThaiTokeniser`.

## Embed a lexicon

Embed the file as an assembly resource and load it through a stream:

```csharp
using var stream = typeof(MyClass).Assembly
    .GetManifestResourceStream("MyNamespace.thai-dict.txt")
    ?? throw new InvalidOperationException("Embedded lexicon was not found.");

var thai = ThaiTokeniser.FromStream(stream);
```

Check the resource name during application startup so a packaging error does not first appear under search load.

## Understand the Japanese codec

> [!WARNING]
> The text format rules do not apply to `japanese.jlc`.

The Japanese codec contains a LeanCorpus FST, compact word costs, unknown-word rules, character classes and connection costs in independently checksummed sections. Its runtime reader is BCL-only and does not read Lucene codec files.

Treat the `.jlc` format as a compatibility-sensitive runtime contract. Replacing it can affect tokenisation, costs and index-visible terms.

## Maintain lexicon assets

Before changing a checked-in file:

1. Record the upstream source and exact version or commit.
2. Record the upstream licence and any required attribution.
3. State whether the checked-in file is source data, generated data or a runtime asset.
4. Use a deterministic conversion process.
5. Compare old and new entry counts and representative tokenisation.
6. Run the matching standalone Rowles.Text tests.
7. Run core `TextIntegration` tests when analysed terms can change.
8. Document compatibility impact for `.jlc` changes.

```bash
./devops test -Suite text
./devops test -Suite core -Area TextIntegration
./devops test -Suite affected
```

There is currently no single repository command that regenerates every lexicon. A replacement change must therefore include enough provenance and conversion detail to be independently reproduced.
