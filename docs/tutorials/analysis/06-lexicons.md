# Lexicons

Several LeanCorpus analysis components use external dictionary files (lexicons).
These are plain UTF-8 text files with one entry per line. They are not embedded
in the library; you download or build them separately and pass a file path.

## Why external lexicons

- Keeps the core library small and avoids shipping data you may not need.
- Lets you use your own curated word lists.
- Makes it easy to update a lexicon without rebuilding.

## Available lexicons

| File | Used by | Source |
|---|---|---|
| `kstem-dict.txt` | `KStemmer` | Derived from Lucene.NET KStem word list (~27,500 entries). |
| `thai-dict.txt` | `ThaiTokeniser` | Starter lexicon of common Thai words. For production, download the ICU `thaidict.txt`. |

Both are available in the repository under the `lexicons/` directory.

## Loading a lexicon from disk

```csharp
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

// KStemmer with file-based lexicon
var lexicon = KStemLexicon.FromFile("lexicons/kstem-dict.txt");
var stemmer = new KStemmer(lexicon);

// Thai tokeniser with file-based lexicon
var thai = ThaiTokeniser.FromFile("lexicons/thai-dict.txt");
```

## Wiring into an analyser

```csharp
// IcuAnalyser with Thai segmentation
var thai = ThaiTokeniser.FromFile("lexicons/thai-dict.txt");
var analyser = new IcuAnalyser(thaiTokeniser: thai);

// Or build the pipeline manually
var tokeniser = new IcuTokeniser(thai);
var customAnalyser = new Analyser(
    tokeniser,
    new LowercaseFilter(),
    new StopWordFilter());
```

## Loading from a stream

If you embed a lexicon as a resource or download it over HTTP, use the `FromStream` API:

```csharp
using var stream = typeof(MyService).Assembly
    .GetManifestResourceStream("MyApp.lexicons.thai-dict.txt");
var thai = ThaiTokeniser.FromStream(stream!);
```

## File format

- UTF-8 encoding
- One entry per line
- Lines starting with `#` are comments and are ignored
- Empty lines are ignored
- Leading and trailing whitespace on each line is trimmed

## See also

- [Analysis overview](index.md)
- [Stemmers](04-stemmers.md)
- [Tokenisers](02-tokenisers.md)
- <xref:Rowles.LeanCorpus.Analysis.Stemmers.KStemLexicon>
- <xref:Rowles.LeanCorpus.Analysis.Tokenisers.ThaiTokeniser>
