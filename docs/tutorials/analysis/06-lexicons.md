# Lexicons

Several components use external dictionary files. These are plain UTF-8 text files, one entry per line. Not embedded; you download or build them separately.

## Why external

Keeps the core library small. Lets you use custom word lists. Easy to update without rebuilding.

## Available lexicons

| File | Used by | Source |
|---|---|---|
| `kstem-dict.txt` | `KStemmer` | Derived from Lucene.NET KStem word list (~27,500 entries) |
| `thai-dict.txt` | `ThaiTokeniser` | Starter lexicon. For production, download the ICU `thaidict.txt` |

Both are in the repository under `lexicons/`.

## Loading

```csharp
// KStemmer
var lexicon = KStemLexicon.FromFile("lexicons/kstem-dict.txt");
var stemmer = new KStemmer(lexicon);

// Thai tokeniser
var thai = ThaiTokeniser.FromFile("lexicons/thai-dict.txt");
```

## Wiring into an analyser

```csharp
var thai = ThaiTokeniser.FromFile("lexicons/thai-dict.txt");
var analyser = new IcuAnalyser(thaiTokeniser: thai);

// Or manually
var tokeniser = new IcuTokeniser(thai);
var custom = new Analyser(tokeniser, new LowercaseFilter(), new StopWordFilter());
```

## From a stream

If you embed a lexicon as a resource:

```csharp
using var stream = typeof(MyService).Assembly
    .GetManifestResourceStream("MyApp.lexicons.thai-dict.txt");
var thai = ThaiTokeniser.FromStream(stream!);
```

## File format

- UTF-8, one entry per line
- `#` starts a comment
- Empty lines ignored
- Leading/trailing whitespace trimmed

## See also

- [Analysis overview](index.md)
- [Stemmers](04-stemmers.md)
- [Tokenisers](02-tokenisers.md)
- <xref:Rowles.LeanCorpus.Analysis.Stemmers.KStemLexicon>
- <xref:Rowles.LeanCorpus.Analysis.Tokenisers.ThaiTokeniser>
