# LeanCorpus Lexicons

Optional data files for language-specific analysis components.
Place these files anywhere on disk and pass the path to the library.

## Available lexicons

| File | Lines | Used by | Licence |
|---|---|---|---|
| `kstem-dict.txt` | ~27,500 | `KStemmer` / `KStemLexicon` | Derived from Lucene.NET KStem word list (Apache 2.0) |
| `thai-dict.txt` | ~200 | `ThaiTokeniser` | Provided as a minimal starter. For production use, download the ICU `thaidict.txt` (Unicode licence) or build your own. |

## Usage

```csharp
// KStemmer with file-based lexicon
var lexicon = KStemLexicon.FromFile("path/to/kstem-dict.txt");
var stemmer = new KStemmer(lexicon);

// IcuTokeniser with Thai segmentation
var thai = ThaiTokeniser.FromFile("path/to/thai-dict.txt");
var tokeniser = new IcuTokeniser(thai);

// IcuAnalyser with Thai segmentation
var analyser = new IcuAnalyser(thaiTokeniser: thai);
```

## Format

- UTF-8 encoded
- One entry per line
- Lines starting with `#` are comments
- Empty lines are ignored
- Entries are trimmed of surrounding whitespace

## Obtaining a larger Thai dictionary

The bundled `thai-dict.txt` is a starter lexicon of ~200 common words for testing.
For production Thai tokenisation, download `thaidict.txt` from the ICU project
(https://github.com/unicode-org/icu) and convert it to the one-entry-per-line format.

## Compile-time or embedded loading

If you prefer to embed a lexicon as a resource, use `FromStream`:

```csharp
using var stream = typeof(MyClass).Assembly
    .GetManifestResourceStream("MyNamespace.thai-dict.txt");
var thai = ThaiTokeniser.FromStream(stream);
```
