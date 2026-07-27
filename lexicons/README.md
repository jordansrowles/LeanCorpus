# LeanCorpus Lexicons

Optional data files for language-specific analysis components.
Place these files anywhere on disk and pass the path to the library.

## Available lexicons

| File | Size or entries | Used by | Licence |
|---|---|---|---|
| `kstem-dict.txt` | ~27,500 | `KStemmer` / `KStemLexicon` | Derived from Lucene.NET KStem word list (Apache 2.0) |
| `thai-dict.txt` | ~200 | `ThaiTokeniser` | Provided as a minimal starter. For production use, download the ICU `thaidict.txt` (Unicode licence) or build your own. |
| `chinese-dict.txt` | ~2,500 | `ChineseLexiconTokeniser` | Common Chinese vocabulary derived from CC-CEDICT data (BSD) |
| `japanese.jlc` | 325,871 surface forms | `JapaneseTokeniser` | Converted from Apache Lucene.NET Kuromoji dictionary data (Apache 2.0) |

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

// Japanese morphological analysis using a downloaded codec
using var japanese = new JapaneseTokeniser("path/to/japanese.jlc");
```

## Format

- UTF-8 encoded
- One entry per line
- Lines starting with `#` are comments
- Empty lines are ignored
- Entries are trimmed of surrounding whitespace

The Japanese codec is binary and does not use the text lexicon format. It
contains a LeanCorpus FST, compact word costs, unknown-word rules, character
classes and connection costs in independently checksummed sections.

## Japanese dictionary

`JapaneseTokeniser` searches parent directories for
`lexicons/japanese.jlc`. The codec was converted from the Apache Lucene.NET
Kuromoji dictionary data. Its runtime reader is BCL-only and does not depend
on Lucene.NET or understand Lucene codec files.

Custom codec paths are loaded lazily. Dispose tokenisers created with a custom
path when they are no longer needed. The default codec is shared for the
process lifetime.

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
