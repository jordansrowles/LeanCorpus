# Slovak comparison cases

`wikimedia-cases.tsv` columns:

- `input` and `wikimedia`: from `SlovakStemmerTest.java` in [wikimedia/search-extra](https://github.com/wikimedia/search-extra) (`opensearch-extra-analysis-slovak`, commit `0202434`). Copyright Wikimedia Foundation, licensed under the Apache License 2.0.
- `leancorpus`: the output of LeanCorpus `SlovakStemmer` at analysis identity `rowles-analysis/sk/0.1.0`.

The test suite pins the `leancorpus` column as a compatibility fixture. The two implementations differ on 19 of the 43 cases. That is comparison evidence, not a conformance claim for either.
