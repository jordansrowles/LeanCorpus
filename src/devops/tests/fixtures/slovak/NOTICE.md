# Slovak comparison cases

`wikimedia-cases.tsv` columns:

- `input` and `wikimedia`: from `SlovakStemmerTest.java` in [wikimedia/search-extra](https://github.com/wikimedia/search-extra) (`opensearch-extra-analysis-slovak`, commit `0202434`). Copyright Wikimedia Foundation, licensed under the Apache License 2.0.
- `leancorpus`: the output of LeanCorpus `SlovakStemmer` at analysis identity `rowles-analysis/sk/0.2.0`.

The test suite pins the `leancorpus` column as a compatibility fixture. The two implementations differ on 20 of the 43 cases (19 at `0.1.0`). That is comparison evidence, not a conformance claim for either.

Rows changed from `0.1.0`:

| Input | `0.1.0` | `0.2.0` | Wikimedia |
| --- | --- | --- | --- |
| `dieťaťom` | `dieť` | `diet` | `dieť` |
| `draľov` | `draľ` | `dral` | `draľ` |
| `političtí` | `političť` | `političt` | `politick` |
| `dokážete` | `dokážeť` | `dokážet` | `dokáh` |
| `zapíšte` | `zapíšť` | `zapíšt` | `zapísk` |
| `cete` | `ceť` | `cet` | `cet` |
| `očami` | `oč` | `ok` | `oča` |
| `vami` | `vami` | `va` | `vam` |
| `najznámejšími` | `najznámejší` | `znám` | `známejš` |
| `najnovšími` | `najnovší` | `nov` | `novš` |

The first six changed because the stem's final consonant is now written hard; `očami` joins `oko`
through the irregular-form table; `vami` loses `-mi` once `-ami` would over-stem; and the
superlatives lose `naj-` and the comparative `-š`.

## Judgements

`judgements.tsv` is the Slovak test oracle. Version 1 was written before the first `0.2.0` rule
changes were chosen; version 2 added the remaining targets (long-vowel nominatives, inserted `ie`,
irregular nouns, `-ém` nouns, adjective degree and verbs) before their rules were compared. Each was
kept fixed while the rules were compared, except that the `robiť` past tense was moved to `report`
before measuring (the `-l` past tense is not removed) and the comparative forms were merged into the
`nový` family so that each form belongs to one family. Its header documents the columns. Forms marked `ud:N` are
attested N times in [UD Slovak-SNK](https://github.com/UniversalDependencies/UD_Slovak-SNK) r2.17
(commit `c3dd2f1`, all three splits, CC BY-SA 4.0); only individual word forms and their counts are
used, not sentences or annotations. Forms marked `paradigm` come from the standard declension and
conjugation tables (for example [kosť](https://www.slovensky.eu/kost/),
[automat](https://www.slovensky.eu/automat/) and [bankomat](https://www.slovensky.eu/bankomat/)).
None of the judgements is a recorded native-speaker review.

`join` families must share one term and `!apart` pairs must share none; `report` families are
measured but not asserted: the `-l` past tense, the `s`/`š` alternation of `písať`, and irregular
comparatives such as `menší`.
