# Stemmers

Stemmers reduce related word forms to a shared root. Improves recall; can reduce precision.

## English stemmers

| Type | Behaviour | Notes |
|---|---|---|
| `EnglishStemmer` | Snowball 3.2.0 Porter2 | Default for English. `AnalyserFactory.Create("en")` and `StemmedAnalyser` use it |
| `PorterStemmer` | Original Porter (1980) | The pre-4.0 `StemmedAnalyser` behaviour. Also `PorterStemmerFilter` and `StemmerAnalyser.Porter()` |
| `LightEnglishStemmer` | Lighter suffix stripping | Less aggressive than Porter |
| `KStemmer` | Lexicon-validated (Krovetz-inspired) | Needs `KStemLexicon.FromFile` |
| `HunspellStemmer` | Hunspell dictionary-based | Needs `.aff`/`.dic` files. Handles irregular forms |

`StemmedAnalyser` applies Porter2 via `EnglishStemmer`. Before 4.0 it used the original Porter algorithm; use `PorterStemmerFilter` for that behaviour.

## Hunspell stemming

```csharp
using Rowles.LeanCorpus.Analysis.Stemmers;

var dict = HunspellDictionary.FromFiles(new MMapDirectory("/dictionaries/en_US"));
var stemmer = new HunspellStemmer(dict);

var analyser = new StemmerAnalyser(
    tokeniser: new Tokeniser(),
    stemmer: stemmer);
```

## Other languages

`ArabicStemmer`, `DutchStemmer`, `FrenchStemmer`, `GermanStemmer`, `ItalianStemmer`,
`PortugueseStemmer`, `RussianStemmer` and `SpanishStemmer` run the official
[Snowball](https://snowballstem.org/) 3.2.0 algorithms. Their output matches Snowball's published
word lists exactly. The code is generated from the Snowball compiler's pinned C# output by
`./devops snowball generate` and is BSD-licensed (see `THIRD-PARTY-NOTICES.md`). They expect
lower-cased input, are safe to share across threads, and do not allocate on the span path.

`SlovakStemmer` is LeanCorpus's own, contributed by Jozef Gajdoš from his
[2014 work on Slovak stemming](https://www.itspy.cz/wp-content/uploads/2015/01/acmspy2014_submission_08.pdf).
Snowball has no Slovak algorithm. It removes case endings and writes the stem's final consonant
hard (`ď`, `ť`, `ň` and `ľ` become `d`, `t`, `n` and `l`), because Slovak spells a soft consonant
without its caron before `e`, `i`, `í` and `ia`. It expects lower-cased NFC input with diacritics:
analysis does not normalise, so decomposed text gives different terms. It also:

- keeps a long `á` or `ú` in a word with no ending, so a masculine nominative joins its other forms
  (`štát`, `štátu`). It shortens `ô`, `ie` and `ŕ`, and `í` before `n`, `t`, `c` or `h`, the genitive
  plurals of `-ina`, `-ita`, `-ica` and `-iha` nouns (`hodín`, `kníh`). Feminine genitive plurals with
  `á` or `ú`, such as `strán`, stay apart from their other forms;
- removes an `ie` inserted in a genitive plural (`okien`, `matiek`) and applies the fleeting-vowel
  rewrites of `-er`, `-ok`, `-ol` and `-ec` to every form (`kostol`, `kostola`);
- joins comparatives and superlatives with their positive (`novší`, `najnovší`, `rýchlejší`), except
  irregular ones such as `menší` and `lepší`;
- joins infinitives with the present tense (`robiť`, `robíme`; `pracovať`, `pracuje`) but not the
  `-l` past tense, which nouns such as `hotel` and `telo` share;
- maps a short list of irregular forms to their family (`dňa`, `psa`, `oči`, `uši`, `ľudia`, `deti`).

`ChineseStemmer`, `JapaneseStemmer` and `KoreanStemmer` are also
LeanCorpus's own.

Use them with `LanguageAnalyser`, `AnalyserFactory.Create(code, stemmer)` or a custom pipeline.

`SnowballStemmer` remains a public base class for custom suffix-table stemmers. The built-in
language stemmers derive from it for compatibility but no longer use its tables.

### Buffer size

`ISpanStemmer.Stem(word, output)` returns `-1` and writes nothing when `output` is too small. A
stem can be longer than its word (German `ß` becomes `ss`), so retry with a larger buffer.
`StemTokenFilter` does this for you, up to `8 * word.Length + 64` characters. A custom stemmer that
needs more makes it throw `InvalidOperationException` rather than emit the word unstemmed.

## Choosing

- `EnglishStemmer` for ordinary English search.
- `LightEnglishStemmer` when Porter is too aggressive.
- `KStemmer` when false stems are worse than missed stems.
- `LanguageAnalyser` for packaged stop words + stemming for a supported language.
- Skip stemming when exact forms matter more than recall.

## Example

```csharp
var analyser = new LanguageAnalyser(
    tokeniser: new Tokeniser(),
    stopWords: StopWords.English,
    stemmer: new EnglishStemmer());
```

## See also

- [Analysis overview](index.md)
- [Analysers](01-analysers.md)
- <xref:Rowles.LeanCorpus.Analysis.Stemmers.ISpanStemmer>
