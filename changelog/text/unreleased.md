# Rowles.Text 2.0.0

Rowles.Text 2.0 is the first standalone release since 1.0.0. It corrects token positions and Unicode handling, adds URL and email tokenisation, explicit concurrency and dictionary ownership contracts and character-filter offset correction, and changes a small number of public APIs where the previous contracts could not support the new behaviour.

### Package versions

- Rowles.Text: `1.0.0` to `2.0.0` ([1c82a2d6b](https://github.com/jordansrowles/LeanCorpus/commit/1c82a2d6bd8e1cea8f257aafc59c377953d26a58))

## Compatibility

- The English, French, German, Spanish, Italian, Portuguese, Dutch, Russian and Arabic stemmers, `AnalyserFactory.Create` for those languages and `StemmedAnalyser` produce different stems. Re-analyse stored output. `AnalyserFactory.GetDefaultAnalysisId` reports the new identities (`rowles-analysis/<code>/3.2.0`). For the earlier `StemmedAnalyser` behaviour, use `PorterStemmerFilter` or `StemmerAnalyser.Porter()`. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- Removing stop words now leaves position gaps, so phrase matching over analysed text no longer joins words across removed tokens. Text analysed and stored by an earlier version must be analysed again from source to get the corrected positions. ([3fb412f5a](https://github.com/jordansrowles/LeanCorpus/commit/3fb412f5aaa035a7184ecd0736bdcf1317ede65a))
- Combining-mark tokenisation, Unicode-scalar handling and `StandardAnalyser` lowercasing change the tokens some text produces. Re-analyse stored output to benefit. ([c35c7644f](https://github.com/jordansrowles/LeanCorpus/commit/c35c7644f217ee1d6cf4272c59ec920f5b403b17), [9ff862ec9](https://github.com/jordansrowles/LeanCorpus/commit/9ff862ec94d8f0f848c889e826a29248b8c970b1), [a861b9832](https://github.com/jordansrowles/LeanCorpus/commit/a861b98323182722c900ca51495b6fb21b7039d6), [1e194acad](https://github.com/jordansrowles/LeanCorpus/commit/1e194acadc77c8ca92b1934d41b3eda5cf89b89f))
- `ICharFilter` now returns `CharFilterResult`; custom character filters must be updated. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))

## Added

- Add `AnalyserFactory.Create(string, ISpanStemmer?)`, which keeps a language's tokeniser and stop words and replaces its stemmer, or removes it with `null`. Requested by [@nordmanndev](https://github.com/nordmanndev) in #123. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- Add `AnalyserFactory.GetDefaultAnalysisId`, which returns an identity such as `rowles-analysis/de/3.2.0` for each default language pipeline. It changes whenever the pipeline's output can change, so callers can tell when to re-analyse. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- Ship `THIRD-PARTY-NOTICES.md` in the package, with the BSD licence for the Snowball algorithms. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- Add Slovak (`sk`) analysis with built-in stop words, `SlovakStemmer` and analyser-factory registration. ([57d7d29a0](https://github.com/jordansrowles/LeanCorpus/commit/57d7d29a03c78a2dba09e15df30a21a649f19a72))
- `Token` carries a positive `PositionLength`, and span sinks and span filters keep graph edges complete, so shingles, synonyms and quoted analysis retain multi-position tokens. Indexing pipelines that cannot store graph edges must flatten non-unit position lengths. ([6b702a035](https://github.com/jordansrowles/LeanCorpus/commit/6b702a035c035d587f81c161a8d6dd0aff81cb15), [da8301733](https://github.com/jordansrowles/LeanCorpus/commit/da830173319d87ab9b9b255b41c3db66e1d14a4f))
- Add `UrlEmailTokeniser` for URL, email, hashtag and mention heuristics. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- Add explicit concurrent-analysis ownership contracts through `IThreadLocalAnalyser`, `IShareableSpanTokeniser` and `IThreadLocalSpanTokeniser`. ([603dffc68](https://github.com/jordansrowles/LeanCorpus/commit/603dffc68a6e0f073126a95479ac7b1ce37e74e3), [ababe3844](https://github.com/jordansrowles/LeanCorpus/commit/ababe384421fc94dff5251b464944b1e7375c4ba))
- Add public disposable `JapaneseDictionary` ownership and `AnalyserFactory.CreateOwnedJapaneseAnalyser` for custom `.jlc` dictionaries. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- `HunspellDictionary` parsing accepts `useCache`, and `ClearCache` releases parsed dictionaries. ([b4998faf5](https://github.com/jordansrowles/LeanCorpus/commit/b4998faf5c214afbcfbf39a3deb4ab0e6d812040))

## Changed

- The English (Porter2), French, German, Spanish, Italian, Portuguese, Dutch, Russian and Arabic stemmers run the official Snowball 3.2.0 algorithms. They match Snowball's published word lists exactly and do not allocate on the span path. The classes keep their names and base class. Reported by [@nordmanndev](https://github.com/nordmanndev) in #122. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- `StemmedAnalyser` uses Porter2 (`EnglishStemmer`) instead of the original Porter algorithm. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- The German stop words include `ans`, `aufs`, `ins` and `vom`. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- `StemTokenFilter` retries with larger buffers when a stem is longer than its word, up to `8 * length + 64` characters, and throws `InvalidOperationException` naming the stemmer if no stem fits. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- Publish a symbol package alongside the Rowles.Text package. ([f02e7c1c4](https://github.com/jordansrowles/LeanCorpus/commit/f02e7c1c4e26be0a571f2a32a79599a98ac5440b))
- Return `CharFilterResult` from `ICharFilter` with public `OffsetCorrectionMap` support for composable UTF-16 offset corrections, mapping token offsets back to the original input across chained transformations. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- Replace path-owning `JapaneseTokeniser` construction with explicit `JapaneseDictionary` ownership; tokenisers borrow supplied dictionaries, and disposal waits for active tokenisation. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- Replace repeated Thai and Chinese lexicon prefix hash probes with an allocation-free radix trie, reducing tokenisation time for long unknown runs. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- Reuse thread-affine execution contexts for the built-in language analyser filters, avoiding per-field filter clones and routing-sink allocations. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- `StemmedAnalyser` and `PorterStemmerFilter` reuse their analysis context instead of allocating one per call. ([415fcdbc7](https://github.com/jordansrowles/LeanCorpus/commit/415fcdbc7c9c78cc7cf7a8f694e87e922b141dd3))
- `SynonymGraphFilter` allocates less per call. ([ca8950666](https://github.com/jordansrowles/LeanCorpus/commit/ca8950666509a82acd74a621af0612fa54c06e0d))

## Fixed

- German stemming no longer throws for words with several `ß`, and German singular and plural forms such as `Tomate` and `Tomaten` share a stem. Reported by [@nordmanndev](https://github.com/nordmanndev) in #122. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- The `ISpanStemmer` documentation no longer says that an output buffer as long as the word always suffices. ([017824fb2](https://github.com/jordansrowles/LeanCorpus/commit/017824fb200f872ee2ef45ccf91633255ba0a10f))
- `LowercaseFilter` lowercases non-ASCII uppercase letters. ([a717abcce](https://github.com/jordansrowles/LeanCorpus/commit/a717abccea6c726c0862400c6f21aee35d52d36b))
- A token-budget rejection is accounted per call, so a rejected input no longer prevents later reuse of the same tokeniser. ([2783e3720](https://github.com/jordansrowles/LeanCorpus/commit/2783e3720084117a2b283794159afe93f75f288f))
- Removing stop words preserves the position gap, so phrase-style matching no longer spans removed words. ([3fb412f5a](https://github.com/jordansrowles/LeanCorpus/commit/3fb412f5aaa035a7184ecd0736bdcf1317ede65a))
- The standard, CJK bigram and Chinese lexicon tokenisers keep combining marks (Unicode Mn, Mc and Me) inside words instead of splitting them off, so Arabic words with harakat and decomposed accented Latin letters stay whole. A mark never starts a token. ([a861b9832](https://github.com/jordansrowles/LeanCorpus/commit/a861b98323182722c900ca51495b6fb21b7039d6))
- Analysis is Unicode-scalar correct while keeping UTF-16 source offsets, including supplementary letters, grams, digits, case boundaries, punctuation and accent marks. Truncation preserves original source offsets, reversal preserves scalar encodings, and span accent normalisation handles isolated surrogates safely. ([1e194acad](https://github.com/jordansrowles/LeanCorpus/commit/1e194acadc77c8ca92b1934d41b3eda5cf89b89f))
- Unicode tokenisers recognise supplementary letters and digits while preserving UTF-16 offsets; unpaired surrogates delimit words. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- `StandardAnalyser` lowercases Unicode tokens invariantly regardless of token length. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- CommonGrams keeps buffered graph edge lengths and combines only adjacent unit-position tokens, and the ICU and URL/email tokenisers keep graph lengths when they delegate Thai runs. ([c84d729ca](https://github.com/jordansrowles/LeanCorpus/commit/c84d729cac40d99fabc99e14f50bfdd2a8f21c05))
- Position lengths and absolute graph edges survive legacy filter routing, cached graph replay and common-gram generation, with independent cache clones. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- Analyser filter and sink state is isolated for concurrent calls, and execution state is cleaned up after a failed analysis. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
- `HunspellDictionary` cache entries are keyed by a content hash, and null text throws `ArgumentNullException`. ([b4998faf5](https://github.com/jordansrowles/LeanCorpus/commit/b4998faf5c214afbcfbf39a3deb4ab0e6d812040))

## Deprecated

- Mark `Uax29UrlEmailTokeniser` obsolete and keep it as a forwarding compatibility wrapper; it does not implement UAX #29. Use `UrlEmailTokeniser` instead. ([f03538876](https://github.com/jordansrowles/LeanCorpus/commit/f035388760fa914e334139962cc288debbf72f76))
