# Slovak comparison cases

`wikimedia-cases.tsv` columns:

- `input` and `wikimedia`: from `SlovakStemmerTest.java` in [wikimedia/search-extra](https://github.com/wikimedia/search-extra) (`opensearch-extra-analysis-slovak`, commit `0202434`). Copyright Wikimedia Foundation, licensed under the Apache License 2.0.
- `leancorpus`: the output of LeanCorpus `SlovakStemmer` at analysis identity `rowles-analysis/sk/0.2.0`.

The test suite pins the `leancorpus` column as a compatibility fixture. The two implementations differ on 20 of the 43 cases (19 at `0.1.0`). That is comparison evidence, not a conformance claim for either.

Rows changed from `0.1.0`, all because the stem's final consonant is now written hard:

| Input | `0.1.0` | `0.2.0` | Wikimedia |
| --- | --- | --- | --- |
| `dieťaťom` | `dieť` | `diet` | `dieť` |
| `draľov` | `draľ` | `dral` | `draľ` |
| `političtí` | `političť` | `političt` | `politick` |
| `dokážete` | `dokážeť` | `dokážet` | `dokáh` |
| `zapíšte` | `zapíšť` | `zapíšt` | `zapísk` |
| `cete` | `ceť` | `cet` | `cet` |

## Judgements

`judgements.tsv` is the Slovak test oracle, written before the `0.2.0` rule changes were chosen and
kept fixed while they were compared. Its header documents the columns. Forms marked `ud:N` are
attested N times in [UD Slovak-SNK](https://github.com/UniversalDependencies/UD_Slovak-SNK) r2.17
(commit `c3dd2f1`, all three splits, CC BY-SA 4.0); only individual word forms and their counts are
used, not sentences or annotations. Forms marked `paradigm` come from the standard declension and
conjugation tables (for example [kosť](https://www.slovensky.eu/kost/),
[automat](https://www.slovensky.eu/automat/) and [bankomat](https://www.slovensky.eu/bankomat/)).
None of the judgements is a recorded native-speaker review.

`join` families must share one term and `!apart` pairs must share none; `report` families are P1
coverage (irregular nouns, long-vowel alternation, verbs and adjective degree) that 4.0 defers.
