
All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.1] - 2026-xx-xx

### Changed
- License changed to Apache 2

### Tests
- Added integration and chaos tests covering previously untested `SegmentReader` methods: `GetFieldLength`, `GetDocIds`, `GetDocFreq`, `GetStoredFields` null path, `GetNumericRange` variants, all DocValues readers, postings methods (`GetPostingsEnumWithPositions`, `GetPositions`, `GetTermFrequency`), pattern-matching methods (`GetTermsMatching`, `IntersectAutomaton`), and vector methods (`GetVector`, `EnsureVectorReaderNoLock`).
- Added chaos property tests for corrupted `.pos`, `.dvn`, and `.vec` codec files to verify structured exceptions are raised rather than silent data corruption.
- Added integration tests covering previously untested `IndexValidator` branches: corrupt migration marker catch block, stale temp file patterns, segment-missing-files path, `.fdt`/`.fdx` magic and version checks, doc count and block offset validation, missing deletion file, live-doc count mismatch, vector/HNSW magic, dimension, and normalisation checks, and deep vector/HNSW validation.
- Added integration tests covering previously untested `IndexSearcher` members: `Metrics` property, `SpanNearQuery`/`SpanOrQuery`/`SpanNotQuery` collection paths, `BlockJoinQuery` with a non-`TermQuery` child (exercises the `BitArray` path in `CollectChildDocsIntoBitArray`), and `VectorQuery` execution.

## [1.3.0] - 2026-05-11

### Added

- Pluggable stored-field compression via the `IFieldCompressionCodec` interface and `CompressionCodecRegistry`; any codec can be registered at startup without modifying core library code.
- BCL codecs for `None`, `Deflate`, and `Brotli` policies using `System.IO.Compression`; available in the core package with no additional dependencies.
- Optional `Rowles.LeanCorpus.Compression.*` packages:
  - `Rowles.LeanCorpus.Compression.LZ4`, via `K4os.Compression.LZ4`.
  - `Rowles.LeanCorpus.Compression.Snappy`, via `Snappier`.
  - `Rowles.LeanCorpus.Compression.Zstandard`, via `ZstdSharp`.
- `Rowles.LeanCorpus.Benchmarks.Compression` project benchmarking compress and decompress throughput across all six policies at three payload sizes (128 B, 4 KB, 64 KB).
- Richer DocValues support with sorted-set (`.dss`), sorted-numeric (`.dsn`), and binary (`.dvb`) sidecars for repeated `StringField`, repeated `NumericField`, and stored-field values, letting facets, grouping fallback, sorting fallback, and numeric aggregations avoid stored-field scans.
- Public index format inventory API through `IndexFormatInspector.Inspect`, reporting commit generation, segment IDs, codec files, codec versions, DocValues sidecars, vector files, HNSW files, live-doc generations, and orphan files.
- Public compatibility API through `IndexCompatibility.Check`, plus reader and writer open guardrails for unsupported future formats, required migrations, and incomplete migration markers.
- Public codec migration API through `IndexCodecMigrator.Plan` and `IndexCodecMigrator.Migrate`, with dry-run planning, staged migration, migration markers, rollback, abandon, and executable rewrites for readable term dictionary, numeric DocValues, sorted DocValues, field-length, and stored-field codec upgrades.
- Public `IndexValidator.Check` API and `System.CommandLine` based `leancorpus-cli.exe` commands for `check`, `inspect`, `compat`, and `migrate`.
- Public `IndexBackup` API and `leancorpus-cli.exe backup`/`restore` commands for manifest-backed snapshot backups. Manifests record commit generation, content token, file names, file lengths, CRC-32 checksums, and file roles before restore validation.
- Maintenance diagnostics for `IndexFormatInspector`, `IndexCodecMigrator`, and `IndexBackup` using `ActivitySource` spans and `Meter` instruments under `Rowles.LeanCorpus`.
- `Rowles.LeanCorpus.Example.NewsgroupsIndexer`, a console example using the shared `bench\data\20newsgroups` data for creating checker test indexes.
- Script documentation and a `send-for-bench.ps1` helper for starting remote Debian benchmark runs in tmux.

### Changed

- Renamed the project, package family, namespaces, command-line tool, scripts, workflows, and documentation to `Rowles.LeanCorpus` and `LeanCorpus`.
- Prepared the optional compression packages for their first NuGet release at version `1.0.0`, with package metadata, README, and licence files.
- Default stored-field compression policy changed from Brotli (via `NativeCompressions`) to `FieldCompressionPolicy.Deflate` using BCL `DeflateStream`.
- Extension package assemblies auto-register their codec via `[ModuleInitializer]` in standard .NET hosts; Native AOT consumers must call `Register()` explicitly at startup.
- `src/` reorganised into `src/core/` (main library and compression packages) and `src/devops/` (benchmarks and tests).
- `SortedNumericDocValues` reduced per-document allocations on the read path by reusing buffers and avoiding redundant array materialisation.
- DocValues presence-bitmap writers now write directly from the underlying `MemoryStream` buffer instead of materialising a new array.
- Postings writer caches `HasFreqs`/`HasPositions` per field and adds a bulk `AddPositions` path to reduce per-token virtual dispatch.
- `IndexSort` serialised field list is pre-computed once per snapshot rather than per segment.
- `StoredFieldsWriter` pools the distinct field-id dedup buffer across documents.
- Pending-delete handling reuses a qualified-term list rather than allocating per flush.
- Norms and field-length writes are fused into a single loop, term-vector dictionaries are built lazily, and DocValues key snapshots are pooled.
- Vector field lookup is pre-built once per writer and DocValues key snapshots are pooled.
- Field-sorted top-N selection now uses `PriorityQueue<TElement, TPriority>` with an `int[]` doc-id map in place of `SortedSet<T>`.
- `DocumentsWriterPerThread` migrated to flat stored-field buffers with a bulk position-merge path, removing per-document list allocations during indexing.
- Zstandard compression now pools compressor and decompressor instances rather than allocating a native wrapper per block.
- `IndexValidator` now reports structured issues with severity, stable codes, segment IDs, file names, and repairability flags, and can opt into deep checks for postings, stored fields, DocValues, vectors, HNSW, and live docs.
- The command-line checker project now builds as `leancorpus-cli.exe`, uses `System.CommandLine`, and supports JSON and file output across validation, inspection, compatibility, and migration commands.
- Commit, segment metadata, live-doc, segment stats, and migration marker writes now share the same temp-file publication helper, and validation reports recognised stale temp files and partial migration markers.
- The test suite is split into `Rowles.LeanCorpus.Tests.Unit`, `Rowles.LeanCorpus.Tests.Integration`, `Rowles.LeanCorpus.Tests.Chaos`, and `Rowles.LeanCorpus.Tests.Shared`, with test sources moved into their owning projects.
- Common target framework, nullable, implicit using, xUnit using, packability, package versions, and NuGet source mapping configuration is centralised through `Directory.Build.props`, `Directory.Packages.props`, and root `NuGet.config`.
- The GitHub build workflow now runs on `main` and `1.3.0`, and includes the compression parity test project.

### Fixed

- `BinaryDocValuesReader`, `SortedNumericDocValuesReader`, and `SortedSetDocValuesReader` now reject corrupt offset tables. Initial offsets must be zero, and binary terminal offsets must equal the total payload length; previously, malformed sidecars could silently skip or expose unrelated bytes.
- LZ4 and uncompressed stored-field codecs now reject impossible compressed/original length combinations instead of accepting corrupt empty or oversized payload metadata.
- Stored-field codec migration now streams existing documents into temporary `.fdt.tmp` and `.fdx.tmp` files before publication, avoiding Windows file-handle conflicts without buffering the whole segment.
- Codec migration now plans richer DocValues rewrites, publishes in-place rewrites through temporary files, removes published staging directories, and marks unexpected migration failures as failed.
- Fuzzy term lookup, searcher refresh observation, and query cache top-N keying now behave deterministically under the full test suite.
- Integration tests migrated from `Assert.True(...Any(...))` to `Assert.Contains` with `StringComparison.Ordinal`; fuzzy-query test corrected to use well-defined edit-distance data.
- Docs site header was missing the Coverage link; `toc.yml` and `docfx.json` updated to include the coverage report.
- Build workflow branch list updated from `1.3.0` to `1.4.0`.
- Benchmark result method names corrected from `LeanCorpus_` to `LeanLucene_`.

### Removed

- Dependency on `NativeCompressions` (preview package) removed from the core library.

## [1.2.1] - 2026-05-05

### Added

- `WhitespaceAnalyser`: splits only on whitespace, preserving punctuation and case; no token filters applied.
- `KeywordAnalyser`: treats the complete field value as a single token.
- `SimpleAnalyser`: letter-only tokenisation with lowercase normalisation and no stop-word removal.
- `WhitespaceTokeniser`: single-pass span scanner splitting on `char.IsWhiteSpace` with correct offsets.
- `KeywordTokeniser`: returns empty or one full-span token.
- `LetterTokeniser`: splits on non-letter characters (digits and punctuation excluded) with correct offsets.
- `LengthFilter`: removes tokens whose text length falls outside an inclusive min/max range; in-place compaction with no extra allocation.
- `TruncateTokenFilter`: truncates token text to a configurable maximum length; adjusts end offset deterministically.
- `UniqueTokenFilter`: removes duplicate tokens globally or within the same position; preserves first occurrence order.
- `DecimalDigitFilter`: normalises Unicode decimal digits (Arabic-Indic, extended Arabic-Indic, full-width) to ASCII digits; no-op tokens are not reallocated.
- `ReverseStringFilter`: reverses token text; single-character tokens are skipped; offsets are unchanged.
- `ElisionFilter`: removes elided article prefixes before straight or curly apostrophes; defaults to the standard French elision set; start offset is adjusted to the post-apostrophe position.
- `KeywordMarkerFilter`: marks configured token texts for stemming bypass in compatible analysers.
- `ShingleFilter`: emits contiguous token shingles with configurable min/max size, optional unigram output, and configurable separator; shingle offsets span the first to last source token.
- `WordDelimiterFilter`: splits compound tokens on delimiter punctuation, case-change boundaries, acronym-word boundaries, and letter-digit boundaries; supports preserve-original, concatenate-words, and concatenate-numbers modes.
- Benchmark suites `analysis-parity` and `analysis-filters` covering lightweight analyser throughput vs Lucene.NET and per-filter allocation on no-op and mutating paths.
- Native AOT compatibility metadata for the core package, with a dedicated `Rowles.LeanCorpus.Example.NativeAot` smoke executable and local `scripts\aot-smoke.ps1` validation script.

### Changed

- Core JSON persistence now uses source-generated `System.Text.Json` metadata for commit files, segment metadata, index stats, segment stats, search analytics, and slow-query log entries.
- Regex-based character filters and regexp queries no longer force compiled regular expressions, improving Native AOT compatibility.
- `StemmedAnalyser` now accepts an optional `KeywordMarkerFilter` to skip stemming for marked tokens.
- `LanguageAnalyser` now accepts an optional `KeywordMarkerFilter` to skip stemming for marked tokens.

### Fixed

- Reduced hot-path allocations in `WhitespaceAnalyser`, `KeywordAnalyser`, and `SimpleAnalyser` by reusing result buffers and caching repeated token text, matching the low-allocation pattern used by `StandardAnalyser`.

## [1.2.0] - 2026-05-04

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

- Default stop word list replaced with the classic 33-word English list (`a`, `an`, `and`, `are`, `as`, `at`, `be`, `but`, `by`, `for`, `if`, `in`, `into`, `is`, `it`, `no`, `not`, `of`, `on`, `or`, `such`, `that`, `the`, `their`, `then`, `there`, `these`, `they`, `this`, `to`, `was`, `will`, `with`). The previous ~95-word list was silently dropping common terms such as `after`, `before`, `could`, and `how` at index time, causing queries for those terms to return no results.

### Added

- `StopWordFilter.ExtendedStopWords` — the previous ~95-word list is now a named, opt-in constant for callers who prefer more aggressive stop word removal.
- `StopWords.EnglishExtended` — convenience alias for `StopWordFilter.ExtendedStopWords`, consistent with the existing `StopWords.*` surface.

### Changed

- `IndexWriterConfig.StopWords` documentation updated to reference `StopWords.EnglishExtended` as the opt-in for aggressive filtering.

> **Re-indexing required** if upgrading from 1.1.2 or earlier with an existing index. Query-time and index-time analysis must use the same stop word set to avoid missed results.

## [1.1.2] - 2026-05-02

### Added

- `stored` parameter on `TextField`, `StringField`, and `NumericField` constructors (defaults to `true`, preserving existing behaviour).
- `StoredField` class for stored-only values without indexing.

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
