---
title: Analysis and language support
_description: Compare LeanCorpus analysers, tokenisers, token filters, stemmers, and language support.
---

# Analysis and language support

Return to the [feature comparison overview](index.md) for status definitions and comparison scope. Individual language components remain listed where their availability is likely to affect adoption.

## Analysers and character filters

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Standard, simple, keyword, and whitespace analysers | ✔ | ✔ | ✔ | Familiar built-in analysis choices. |
| Custom analyser composition | ✔ | ✔ | ✔ | Compose tokenisation and filtering pipelines. |
| Per-field analysis | ✔ | ✔ | ✔ | Index-time analyser selection by field. |
| ICU analysis | ✔ | ✔ | ✔ | Unicode segmenter-backed analysis. |
| Language and stemmed analysers | ✔ | ✔ | ✔ | Language selection and generic stemmer composition. |
| Span-based analysis API | ✔ | ❌ | ❌ | Low-allocation tokenisers, filters, and sinks. |
| Index-time token budgets | ✔ | ❌ | ❌ | Truncate or reject documents exceeding configured limits. |
| HTML stripping, mapping, and pattern replacement | ✔ | ✔ | ✔ | Ordered character filtering before tokenisation. |
| Collation-key analysis | ❌ | ❌ | ✔ | No locale-aware collation-key analyser. |
| Query auto-stop-word analysis | ❌ | ❌ | ✔ | No automatic high-frequency query-term suppression. |

## Tokenisers

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Standard, keyword, letter, whitespace, and pattern tokenisers | ✔ | ✔ | ✔ | General-purpose tokenisation. |
| N-gram and edge n-gram tokenisers | ✔ | ✔ | ✔ | Substring and prefix indexing. |
| URL and email tokenisation | ✔ | ✔ | ✔ | URL, email, hashtag and mention preservation with Unicode-aware word heuristics; no UAX #29 conformance claim. |
| Path hierarchy tokenisation | ✔ | ✔ | ✔ | Prefix and suffix modes with depth payloads. |
| CJK bigram tokenisation | ✔ | ✔ | ✔ | CJK bigram support. |
| Chinese lexicon tokenisation | ✔ | ✔ | ✔ | Longest-match segmentation with unigram fallback. |
| Japanese morphological tokenisation | ✔ | ✔ | ✔ | Dictionary-backed least-cost segmentation. |
| Thai tokenisation | ✔ | ✔ | ✔ | Thai word boundary support. |
| MediaWiki tokenisation | ✔ | ✔ | ✔ | Comparable to Lucene's Wikipedia tokeniser. |
| Legacy classic tokeniser | ❌ | ✔ | ✔ | Use the standard tokeniser for new applications. |

## Token filters

| Feature group | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Case, accent, decimal-digit, and elision normalisation | ✔ | ✔ | ✔ | Includes lowercasing and accent folding. |
| Stop words, length, truncation, and keep-word filtering | ✔ | ✔ | ✔ | Common token selection controls. |
| Synonym graphs and word delimiters | ✔ | ✔ | ✔ | Graph-aware synonyms and delimiter processing. |
| Shingles and common grams | ✔ | ✔ | ✔ | Phrase-oriented token generation. |
| Stemming and Hunspell filtering | ✔ | ✔ | ✔ | Generic, Porter, and dictionary-backed stemming. |
| Keyword marking and token caching | ✔ | ✔ | ✔ | Protect terms from stemming and replay token streams. |
| Phonetic matching | ✔ | ✔ | ✔ | Metaphone and bounded phonetic alternatives. |
| Pattern replacement and reverse-string filtering | ✔ | ✔ | ✔ | Term transformation utilities. |
| Unique, type, and hyphenated-word filtering | ✔ | ✔ | ✔ | Common cleanup and selection filters. |
| Compound-word decomposition | ❌ | ◐ | ✔ | Dictionary and hyphenation decomposition are not available. |
| Payload construction filters | ❌ | ◐ | ✔ | Payloads are supported in postings, but specialised payload filters are absent. |
| Specialised graph and conditional filters | ❌ | ❌ | ✔ | Includes concatenate-graph, protected-term, and conditional filtering. |
| Scandinavian and Indic normalisation | ❌ | ◐ | ✔ | Specialised normalisers remain absent. |
| MinHash and Word2Vec synonym filters | ❌ | ❌ | ✔ | No equivalent specialised filters. |

## Stemmers

| Language or algorithm | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Arabic | ✔ | ✔ | ✔ | `ArabicStemmer`. |
| Dutch | ✔ | ✔ | ✔ | `DutchStemmer`. |
| English Porter and Snowball | ✔ | ✔ | ✔ | `EnglishStemmer`. |
| English Krovetz and light stemming | ✔ | ✔ | ✔ | `KStemmer` and `LightEnglishStemmer`. |
| French | ✔ | ✔ | ✔ | `FrenchStemmer`. |
| German | ✔ | ✔ | ✔ | `GermanStemmer`. |
| Italian | ✔ | ✔ | ✔ | `ItalianStemmer`. |
| Portuguese | ✔ | ✔ | ✔ | `PortugueseStemmer`. |
| Russian | ✔ | ✔ | ✔ | `RussianStemmer`. |
| Spanish | ✔ | ✔ | ✔ | `SpanishStemmer`. |
| Slovak | ✔ | ✔ | ✔ | `SlovakStemmer`. |
| Chinese | ◐ | ✔ | ✔ | Segmentation is handled by `ChineseLexiconTokeniser`; the stemmer is a no-op adapter. |
| Japanese | ◐ | ✔ | ✔ | Segmentation is handled by `JapaneseTokeniser`; the stemmer is a no-op adapter. |
| Korean | ◐ | ✔ | ✔ | Uses CJK bigram tokenisation; the stemmer is a no-op adapter. |
| Hindi | ❌ | ✔ | ✔ | Not currently available. |
| Turkish | ❌ | ✔ | ✔ | Not currently available. |

## Unicode scalars and source offsets

Rowles.Text 3.0 and LeanCorpus 4.0 use Unicode scalar values for letter, digit,
case and punctuation classification. NGram and EdgeNGram gram sizes, LengthFilter
bounds and TruncateTokenFilter limits count scalars, not UTF-16 code units.
Token offsets remain UTF-16 indices into the original source. Truncation preserves
both incoming source offsets, even when preceding filters have changed text length.

Malformed UTF-16 is processed deterministically: each isolated surrogate consumes
one code unit, is not assigned a Unicode category, and is copied unchanged by text
transformations. Valid surrogate pairs are never split. ReverseStringFilter reverses
scalars, not grapheme clusters. Reversing valid text twice restores it exactly;
reversing broken surrogate units can join them into a valid pair, so malformed text
need not have that property.

AccentFoldingFilter performs canonical decomposition, removes non-spacing-mark
scalars, then performs canonical composition. It is not a general ASCII
transliterator: for example, `ø` remains unchanged. Valid runs on either side of an
isolated surrogate are normalised independently. These implementations use .NET
10/11 span and Rune APIs, retaining allocation-free ASCII paths and pooled scratch
buffers for longer transformations.

Existing indexes are not automatically rewritten. An index built with an affected
older analysis configuration may need reindexing to restore exact index/query
analyser parity, particularly for supplementary text and the corrected truncation
semantics. These changes introduce no codec format change or postings migration.
