<!-- filename: `version - YYYY-mm-dd` -->

### Added

- `PathTreeTokeniser` emits compound path tokens from root to leaf (forward mode) or leaf to root (suffix mode), with root-aware parsing for drive letters, UNC paths, and scheme URIs. Supports optional depth payloads for shallow-matching boosts and inline ASCII case normalisation (d5b6b1cbd)
- `FunctionQuery` and `DoubleValuesSource` support scores, double and Int64 fields, constants and composed arithmetic (cf9c49a10)
- `FunctionScoreQuery` can now use composed `DoubleValuesSource` values while retaining its numeric-field fast path (cf9c49a10)
- `SpanFirstQuery`, `SpanContainingQuery`, `SpanWithinQuery`, `FieldMaskingSpanQuery` and `SpanMultiTermQueryWrapper` extend positional matching (cf9c49a10)
- `AnalysingQueryParser` analyses wildcard, prefix, fuzzy and range literals (cf9c49a10)
- `ComplexPhraseQueryParser` supports alternatives and multi-term expansion inside quoted phrases (cf9c49a10)
- `TermsQuery` performs exact UTF-8 term-set matching without string conversion during dictionary lookup (cf9c49a10)
- `ChineseLexiconTokeniser` performs greedy longest-match segmentation using built-in or file-based dictionaries (06aec67a6)
- `JapaneseTokeniser` performs dictionary-backed least-cost Viterbi segmentation with reusable zero-allocation warm-path storage (06aec67a6)
- Japanese dictionaries use a versioned and checksummed `.jlc` file containing the FST, word costs, unknown-word rules, character classes and connection costs (06aec67a6)
- `FstReader` provides an internal allocation-free prefix cursor without changing existing query lookup paths (06aec67a6)
- `QueryParser` now supports ranges, AND, OR, NOT, field-exists queries, regex, constant scores and `DisMax` groups (9f7e16ce4)
- `BooleanQuery` now supports a configurable minimum number of matching SHOULD clauses (9f7e16ce4)
- `SynonymQuery` now scores alternative terms as one blended query (9f7e16ce4)
- Custom queries can now use rewrite, visitor, weight and scorer extension points (9f7e16ce4)
- Collectors can now receive per-segment context and access the current scorer (9f7e16ce4)
- `PhraseQuery` now supports explicit term positions (9f7e16ce4)
- Numeric ranges now support inclusive and exclusive bounds (9f7e16ce4)
- Int32, float, binary and IP address range and point-set queries are now available (9f7e16ce4)
- Search now supports per-field similarity models (9f7e16ce4)
- Search now supports multi-level sorting, search-after pagination and min or max selection for multi-valued fields (9f7e16ce4)
- `QueryRescorer` and `SortRescorer` can now re-rank an existing result set (9f7e16ce4)
- Suggestions now support analysers, query-based context filtering and free-text next-term completion (9f7e16ce4)
### Changed

- `SearchAfter` now retains only the requested next-page candidates for score, document-ID and multi-field sort cursors (cf9c49a10)
- `QueryRescorer` now retains second-pass scores only for first-pass candidates and supports separate weights and custom score combination (cf9c49a10)
- Japanese analysis now uses morphological tokens and Japanese stop-word filtering instead of bigram segmentation (06aec67a6)
- Chinese analysis now uses lexicon segmentation with Chinese stop-word filtering instead of ideograph bigrams (06aec67a6)
- Korean analysis now keeps Hangul word runs intact and applies Korean stop-word filtering (06aec67a6)
### Fixed

- `CJKBigramTokeniser` now distinguishes ideographs from kana and Hangul and preserves supplementary CJK characters as complete UTF-16 code points (06aec67a6)
- Custom collectors now receive every matching document instead of stopping at 1,024 hits (9f7e16ce4)
### Removed
### Deprecated
### Security