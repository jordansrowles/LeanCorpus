
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.2.0] - 2026-05-03

### Added

- `SearchOptions` execution controls for per-query timeouts, scoring-budget limits, and streaming result collection, with `TopDocs.IsPartial` and `ScoreDoc.EstimatedBytes` exposing partial-result and resource-use information.
- Field validation for document field names and vector inputs, plus coverage for invalid field, vector, and geo-point values.
- BKD-backed numeric range search with sparse numeric-index sourcing and brute-force fallback.
- Streaming merge infrastructure for postings, stored fields, term vectors, and doc-value flushing to reduce merge-time memory pressure.
- Concurrent writer support for vectors, geo points, and sorted doc values.
- Codec validation for RoaringBitmap and segment files, including headers, CRC32 footers, and orphan sidecar cleanup.
- Fault-injection, merge-equivalence, writer-equivalence, concurrent-field, and force-merge memory tests, plus a BKD numeric range benchmark.

### Changed

- Field-sorted search now uses heap selection for top-N results rather than sorting the full candidate set.
- Segment merges now run IO outside the writer lock so indexing can continue during merge work.
- Query parsing now reports offsets more accurately and supports lenient parsing behaviour.
- `DisjunctionMaxQuery` instances are frozen for safer execution, and segment metadata snapshots clone all tracked fields.
- Numeric doc-value range computation now compares mixed-sign bit patterns in unsigned space.

### Fixed

- `PrefixQuery` and `WildcardQuery` now return each matching document once when multiple matched terms occur in the same document.
- Durable commits now fail closed when fsync fails instead of reporting success.
- Recovery validates required segment files and codec headers, fails closed when no commit file is valid, and tolerates missing `.del` files for graceful degradation.
- Streaming postings merge now seeks the position section correctly during force merges.
- Concurrent indexing now preserves vector, geo-point, and sorted doc-value data.
- `MMapDirectory` now implements disposal correctly, and pending delete handling no longer drops deletion state.
- README collapse and per-query resource-control examples were corrected.

## [1.1.3] - 2026-05-03

### Fixed

- Default stop word list replaced with Lucene.NET's classic 33-word `ENGLISH_STOP_WORDS_SET` (`a`, `an`, `and`, `are`, `as`, `at`, `be`, `but`, `by`, `for`, `if`, `in`, `into`, `is`, `it`, `no`, `not`, `of`, `on`, `or`, `such`, `that`, `the`, `their`, `then`, `there`, `these`, `they`, `this`, `to`, `was`, `will`, `with`). The previous ~95-word list was silently dropping common terms such as `after`, `before`, `could`, and `how` at index time, causing queries for those terms to return no results.

### Added

- `StopWordFilter.ExtendedStopWords` — the previous ~95-word list is now a named, opt-in constant for callers who prefer more aggressive stop word removal.
- `StopWords.EnglishExtended` — convenience alias for `StopWordFilter.ExtendedStopWords`, consistent with the existing `StopWords.*` surface.

### Changed

- `IndexWriterConfig.StopWords` documentation updated to reference `StopWords.EnglishExtended` as the opt-in for aggressive filtering.

> **Re-indexing required** if upgrading from 1.1.2 or earlier with an existing index. Query-time and index-time analysis must use the same stop word set to avoid missed results.

## [1.1.2] - 2026-05-02

### Added

- `stored` parameter on `TextField`, `StringField`, and `NumericField` constructors (defaults to `true`, preserving existing behaviour).
- `StoredField` class for parity with Lucene.NET — stores a value without indexing it.

### Fixed

- `PrefixQuery` and `WildcardQuery` used as sub-clauses inside a `BooleanQuery` now return results correctly. The `ExecuteSubQuery` dispatch switch was missing cases for both query types, causing all intersections to silently return empty results.
- `SearcherManager` background-refresh test was susceptible to a race condition when using the default 1-second refresh interval; test now uses a 5-minute interval to keep the background loop dormant during assertions.

## [1.1.1] - 2026-05-01

### Added

- CHANGELOG.md

### Fixed

- `m*rket` forces a broad body\0m... scan and decodes rejected terms before matching.

### Changed

- Wildcard term matching no longer decodes every rejected `body\0m...` candidate into a string.
- `FSTReader` now has a low-allocation offset path for wildcard search, uses ASCII byte matching for
ASCII patterns/terms, and only falls back to string decoding when needed to preserve existing non-ASCII `?` semantics.
