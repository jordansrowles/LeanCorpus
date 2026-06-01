

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [1.5.0] - Work In Progress

## [1.4.0] - 2026-05-29

### Added

- Soft deletes with configurable retention period. When `IndexWriterConfig.SoftDeletesEnabled` is `true`, `SoftDeleteDocuments(TermQuery)` marks matching documents as deleted in the live-docs bitmap and records a Unix-millisecond timestamp in the `.del` file. Soft-deleted documents are invisible to search but retained on disk until the retention period elapses, at which point merges reclaim the space.
- Per-segment sequence number tracking. When `IndexWriterConfig.TrackSequenceNumbers` is `true`, each document is assigned a monotonically-increasing sequence number and the segment metadata records `MinSequenceNumber` and `MaxSequenceNumber`. `IndexWriter.NextSequenceNumber` exposes the next sequence number that will be assigned.
- `UpdateDocuments(Query, LeanDocument)` for atomically deleting documents matching a query and adding a replacement. Supports `TermQuery`, `BooleanQuery` of `TermQuery` clauses, and `MatchAllDocsQuery`.
- `IndexWriter.AddIndexes(MMapDirectory)` to merge all segments from a source directory into the current index. Segments are validated for format compatibility and merged into a single new segment without modifying the source files.
- `HunspellDictionary` now supports character ranges (`[a-z]`, `[A-Z]`, `[0-9]`) in affix conditions; `AF` alias directive parsing; morphological tag extraction from dictionary entries (`word/flags po:verb`); thread-safe content-hash-based dictionary caching across repeated `Parse` calls; and `FromFile`/`FromStream` convenience overloads.
- `StemTokenFilter` wraps any `IStemmer` as a composable `ITokenFilter` for use in the `Analyser` pipeline.
- `StemmerAnalyser` provides a generic analyser pipeline (tokenise → lowercase → stopwords → stem) accepting any `IStemmer`, with factory methods for Porter, KStemmer, LightEnglish, and Hunspell backends.
- `PorterStemmer` is now a public `IStemmer` adapter (previously internal via `PorterStemmerFilter.Stem`).
- `LightEnglishStemmer` now has comprehensive unit tests covering irregular forms, plurals, past tense, progressive, derivational suffixes, protected words, and e-restoration.
- Benchmark suites: `KStemmerParityBenchmarks`, `HunspellBenchmarks`, and `LightEnglishStemmerBenchmarks` (accessible via `--suite kstemmer`, `--suite hunspell`, `--suite lightenglish`).
- A Roslyn source generator (`Rowles.LeanCorpus.SourceGen`) that turns `[LeanDocument]`-annotated models into typed `LeanDocumentMap<T>`s with `ToDocument`, `FromStoredDocument`, `CreateSchema`, and `Fields` descriptors via direct, reflection-free, AOT-friendly code; ships attributes (`LeanText`, `LeanString`, `LeanNumeric`, `LeanVector`, `LeanGeoPoint`, `LeanStored`, `LeanIgnore`) and the `Mapping` runtime surface (`LeanDocumentMap<T>`, `LeanFieldBinding<T>`, `LeanField<T,V>`, `StoredDocument`, `LeanGeoLocation`, `LeanNumericEncoding`, `LeanNumericEncoders`) in the core library, with diagnostics `LCGEN001`–`LCGEN013`, strict-schema defaults, materialiser safety checks, stored round-tripping guidance, and a dedicated test project exercising generator output, diagnostics, nullability rules, encoder round-trips, and map round-trips.
- Unicode-aware analysis components: `IcuAnalyser`, `IcuTokeniser`, `Uax29UrlEmailTokeniser`, `ThaiTokeniser`, `MediaWikiTokeniser`, `KeepWordFilter`, `TypeTokenFilter`, `LimitTokenCountFilter`, `FlattenGraphFilter`, `MetaphoneFilter`, `PhoneticAlternatesFilter`, `HunspellStemFilter`, `LightEnglishStemmer`, and a lexicon-backed `KStemmer`.
- Span-backed analysis APIs (`SpanToken`, `ISpanTokeniser`, `ISpanAnalyser`, `ISpanTokenSink`, `ISpanTokenFilter`) for zero-allocation tokenisation; `Tokeniser` implements `ISpanTokeniser` and `StandardAnalyser` implements `ISpanAnalyser`, feeding tokens directly to `SpanPostingTokenSink` without allocating `List<Token>` or per-token strings. `Analyser` constructor accepts `ISpanTokeniser` directly; a static `Analyser.FromTokeniser(ITokeniser, ...)` factory wraps legacy tokenisers in a span adapter.
- `BinaryField` for stored raw byte values, with typed stored-field codec support, binary doc-values mirroring, and binary retrieval through `SegmentReader` and `IndexSearcher`.
- Index-time field boosting on document fields, persisted through norms, applied across text, boolean, range, vector, and geo scoring paths, and surfaced in score explanations.
- Payload-bearing term vectors and payload-preserving merge paths for postings and stored term vectors.
- `MatchAllDocsQuery`, `MatchNoDocsQuery`, `FieldExistsQuery`, `TermInSetQuery`, `PointInSetQuery`, `MultiPhraseQuery`, `IntervalsQuery`, and `CombinedFieldsQuery`, with execution support in `IndexSearcher`, order-stable query-cache fingerprints, BKD exact-set lookup, and stored-only field-existence fallback.
- Async indexing APIs on `IndexWriter` for single-document, batched, block, and commit workflows, using cancellation-aware backpressure waits while preserving the existing synchronous indexing core.
- Streamed bulk ingestion from `IAsyncEnumerable<LeanDocument>` with bounded batching.
- A real `FstReader` over the FST1 blob format with arc-walk exact lookup, prefix enumeration, automaton intersection, and allocation-light `CollectIntersectOutputs` / `CollectOutputsWithPrefix` / `CollectContainsOutputs` overloads; extended `LevenshteinAutomaton` with `MinDistance(state)` so fuzzy callers can recover edit distance from the FST traversal.
- Regexp query execution extracts a literal prefix from simple patterns like `gov.*ment` or `mark.*` and enumerates only the matching FST subtree, avoiding full field enumeration and the associated allocation explosion. A new `IAutomaton.IsSink` default method lets the FST intersection path bail into a fast output-collection traversal when the automaton enters a fully-permissive state, improving wildcard throughput for patterns with interior `*` after early literal matches.
- Offsets-only prefix enumeration for prefix and trailing-wildcard query execution when global document-frequency remapping is not needed.
- Span-sink n-gram benchmark variants to measure the allocation-aware tokenisation path separately from the legacy `List<Token>` API.
- Added these unit tests:
  - `StringField` and `TextField` null-value guard, `FieldType`, and `IsIndexed` branches.
  - `BooleanClause` equality: null object, wrong type, null typed, differing query, differing occur, equal instances, and consistent hash code.
  - `AggregationRequest` null-name and null-field guards, and for `AggregationResult.Avg` zero-count path and `Empty` factory.
  - `SegmentInfo.ReadFrom` with a JSON `null` literal, verifying `InvalidDataException` is raised.
  - `InMemoryVectorSource`: `Count` property, `GetVector` hit, `GetVector` miss (`KeyNotFoundException`), and null-dictionary guard.
  - `CompressionCodecRegistry.TryGet` false path and `Get` unregistered-policy throw.
  - `GeoDistanceQuery` equality: differ-by-field, differ-by-CentreLat, differ-by-CentreLon, `Equals(null)`, and `Equals(wrong-type)`.
  - `IndexInputEdgeCaseTests` covering EOF throws across all primitive readers (ref and non-ref), all five unrolled `ReadVarIntFast` byte-length paths, the fallback path, VarInt mid-decode EOF and overflow, corrupt UTF-8 sequences (3-byte, 4-byte, truncated, bytes-exhausted), heap-allocation path for charCount > 256 in `ReadUtf8String` and `CompareCharsAndAdvance`, and `Prefetch` on empty and non-empty files.
  - `IndexFormatInspectionOptions.IncludeChecksums`, `IndexFileInspector` commit-file discovery and all error branches of `TryReadCommit` and `CheckCodecHeader`, and `VectorFilePaths.Sanitise` covering all character-substitution and heap-allocation branches.
  - FST reader allocation-light output-collector paths (`CollectOutputsWithPrefix`, `CollectIntersectOutputs`, `CollectContainsOutputs`) and outputs-only enumeration (`EnumerateOutputsWithPrefix`, `EnumerateContainsOutputs`, `IntersectAutomatonOutputs`), covering prefix, wildcard, Levenshtein, IsSink fast-path, and field-qualifier overloads.
  - FST reader edge cases: corrupt-blob rejection (truncated header, wrong magic, out-of-bounds node address), large VarInt output round-trip (byte boundaries through to `long.MaxValue`), final-output virtual arc (0xFF label for nodes that are both final and have child sub-keys), and deep-FST round-trip with 100 keys of ~1KB each.
  - FST builder edge cases: VarInt byte boundaries from 0 through `long.MaxValue`, complex output distribution through nested prefix keys ("a"/"ab"/"abc"/"abd"), and frontier capacity growth with 100 keys of ~10KB each.
  - FST automaton edge cases: Levenshtein with maxEdits=5 against 220+ terms verified against brute-force, complex wildcard patterns (multiple `*`/`?` mixed, Unicode `caf*`), multi-byte UTF-8 prefix boundaries, and `MinDistance` on dead/non-matching states.
  - `SegmentReader` pattern matching: `GetFuzzyMatches` with edit distance, `GetTermsMatchingRegex` with compiled regex, and `GetTermsInRange` with inclusive bounds.
- Added these integration tests:
  - `SegmentReader` methods: `GetFieldLength`, `GetDocIds`, `GetDocFreq`, `GetStoredFields` null path, `GetNumericRange` variants, all DocValues readers, postings methods (`GetPostingsEnumWithPositions`, `GetPositions`, `GetTermFrequency`), pattern-matching methods (`GetTermsMatching`, `IntersectAutomaton`), and vector methods (`GetVector`, `EnsureVectorReaderNoLock`).
  - `IndexValidator` branches: corrupt migration marker catch block, stale temp file patterns, segment-missing-files path, `.fdt`/`.fdx` magic and version checks, doc count and block offset validation, missing deletion file, live-doc count mismatch, vector/HNSW magic, dimension, and normalisation checks, and deep vector/HNSW validation.
  - `IndexSearcher` members: `Metrics` property, `SpanNearQuery`/`SpanOrQuery`/`SpanNotQuery` collection paths, `BlockJoinQuery` with a non-`TermQuery` child (exercises the `BitArray` path in `CollectChildDocsIntoBitArray`), and `VectorQuery` execution.
  - `SimdIntrinsicsVectorOps` AVX-512 paths (`CosineAvx512`, `DotAvx512`; conditionally skipped when unsupported) and three new `IndexCodecMigrator` paths: non-executable plan, pre-migration validation failure, and auto-generated staging directory.
  - `IndexStats.TryLoadFrom` with a JSON `null` literal returning null.
  - `SearcherManager` refresh-failure paths: `LastRefreshError`, `LastRefreshErrorAt`, `ConsecutiveRefreshFailures`, `RefreshFailed` event, subscriber-exception guard, and counter reset after recovery.
  - New query families: BM25F helper logic, multiphrase slot alternates, interval span semantics, set-query fingerprinting, BKD fallback on corrupt point trees, and explicit corruption failure paths for stored fields and positional queries.
  - Binary fields, boost scoring, merge round-trips, and truncated payload and boost tails.
  - Unicode-aware analysis components: extensible token types, MediaWiki token classes, phonetic alternates, Hunspell stemming, and token-budget guardrails.
  - Async ingestion, source-failure retention semantics, backpressure cancellation, and async block indexing.
- Added these chaos tests:
  - Corrupted `.pos`, `.dvn`, and `.vec` codec files verifying structured exceptions are raised rather than silent data corruption.
  - `IndexCodecMigrator`: read-only `.dic` forcing the exception catch path, and a staged migration where a corrupted `.nrm` triggers `ValidateAfterMigration` failure.
  - `IndexStats.WriteTo`: read-only destination fires the `UnauthorisedException` catch block and cleans up the tmp file; file-locked destination fires the `IOException` catch block.

### Changed

- License changed to Apache 2.
- **Breaking:** `NGramTokeniser` and `EdgeNGramTokeniser` no longer implement `ITokeniser`; they only expose the zero-allocation `ISpanTokeniser` path and the stack-only `EnumerateTokens` enumerator. The legacy `List<Token>`-based methods have been removed. Whitespace scanning in split-aware paths (edge n-grams and `SplitOnWhitespace` n-grams) now happens inline instead of allocating a temporary `List<(int,int)>` per call. The `MaterialisingTokenSink` is used internally by `Analyse()` to produce the `List<Token>` output. NGram tokeniser benchmark suite has been pruned to the SpanSink, Streaming, and Lucene.Net comparison paths only.
- `NGramTokeniser` now accepts a `splitOnWhitespace` constructor parameter (default `false`). When `true`, n-grams are generated per whitespace-delimited word rather than across the full input, eliminating cross-word-boundary grams and dramatically reducing allocations for larger gram ranges.
- Removed the redundant pre-count pass from the `Tokenise(input, tokens)` buffer overload of both `NGramTokeniser` and `EdgeNGramTokeniser`; the reused list's existing capacity is sufficient after warmup and the O(n) scan is no longer performed on every call. The allocating `Tokenise(input)` overload retains its pre-count for correct initial sizing.
- Added `LeanCorpus_NGramTokeniser_WordSplit` benchmark variant to `NGramTokeniserBenchmarks` to surface the per-word splitting path in benchmark runs.
- The qualified-term interning and postings dictionary lookup are merged into a single alternate-lookup probe per token, eliminating the double hash computation. `FstBuilder.EnsureNodeCapacity` lets `TermDictionaryWriter` pre-size the suffix-sharing registry to the unique term count, avoiding rehashing during FST construction.
- Extracted `SegmentFlusher` as a standalone static class, consolidating ~25 buffer collections from `IndexWriter` into a single `DocumentBufferState` class. `IndexWriter.SegmentFlush.cs` (668 lines) is removed; all flush logic now lives in `SegmentFlusher.Flush()`. `SpanPostingTokenSink` now references `DocumentBufferState` directly, eliminating the `IndexWriter` back-reference from the token sink. Zero allocation impact — `DocumentBufferState` is allocated once in the constructor, same as the previous scattered initialisers.
- Cut the on-disk term dictionary over to a real FST (Daciuk minimal acyclic transducer) with the new v3 `.dic` format. Exact lookups become O(term length) arc walks with shared-prefix memory; prefix, wildcard, and fuzzy queries are now native FST × automaton intersections. v1 and v2 dictionaries are no longer opened by the live read path; `TermDictionaryReader.Open` throws an `InvalidDataException` with a "run leancorpus-cli migrate" hint, and `IndexCodecMigrator` upgrades them in place via the legacy readers held under `Codecs\TermDictionary\Legacy\`.
- Switched fuzzy matching to a byte-level (UTF-8) Levenshtein automaton. For ASCII queries this is identical to the previous char-level distance; for queries containing multi-byte code points the reported edit distance now counts UTF-8 byte edits rather than character edits.
- Renamed the real FST builder `FiniteStateTransducerBuilder` to `FstBuilder` and moved the legacy v2 byte-array term dictionary (formerly misnamed `FSTReader`/`FSTBuilder`) and the v1 reader to `Codecs\TermDictionary\Legacy\` for migrator-only use.
- Moved `kstem-dict.txt` out of the embedded resources into `lexicons/` at the solution root. `KStemmer` no longer has a parameterless constructor; provide a `KStemLexicon` loaded via `KStemLexicon.FromFile` or `KStemLexicon.FromStream`. `KStemLexicon.Default` and `KStemLexicon.FromEmbeddedResource` are removed.
- Moved the built-in Thai lexicon out of `ThaiTokeniser` into `lexicons/thai-dict.txt`. `ThaiTokeniser` no longer has a parameterless constructor; provide a lexicon via the constructor, `ThaiTokeniser.FromFile`, or `ThaiTokeniser.FromStream`.
- Decoupled `IcuTokeniser` and `Uax29UrlEmailTokeniser` from `ThaiTokeniser`. Both now accept an optional `ITokeniser` for Thai segmentation via their constructor. Without injection, Thai characters are treated as regular word characters.
- `MediaWikiTokeniser` now caches its `IcuTokeniser` instance as a field rather than allocating a new one per markup block.
- `MediaWikiTokeniser` now accepts an optional `Uax29UrlEmailTokeniser` parameter so the body-text tokeniser between markup blocks can be injected.
- Replaced the enum-based `TokenKind` analysis contract with string token types, removed the public generic `TokenTypes` taxonomy, and moved producer-specific token type names onto the tokenisers that emit them.
- Tightened new query constructors so empty fields, empty term groups, unknown combined-field weights, and non-finite point values fail fast instead of being silently filtered or coerced.
- Scoped lightweight phonetic and English stemming APIs to honest names, and added Hunspell condition parsing plus generated-form limits.
- Hardened async and batch indexing so schema validation runs before slot acquisition, dispose drains active indexing operations, block indexing suppresses mid-block threshold flushes until the parent marker is present, and partial indexing failures make the writer unusable until reopened.
- Precomputed `CombinedFieldsQuery` union document frequencies once per search execution and bounded `TermInSetQuery` term counts.
- Stopped mirroring stored `TextField` values into binary DocValues by default, keeping binary DocValues for `BinaryField`, `StoredField`, and exact `StringField` values.
- Changed norms boost storage to sparse entries so default field boosts do not write or load per-document `float[]` arrays.
- Changed phrase query execution to intersect candidate documents before decoding positional data for common multi-term phrases.
- Hardened benchmark suites by splitting block-join and deletion workloads, broadening Boolean and fuzzy query scenarios, aligning Lucene.NET disk-backed comparison paths, and making suite selection fail fast on unknown names.
- `IndexCodecMigrator` now tolerates `LLIDX033`/`LLIDX034` validation errors caused by an outdated term dictionary on segments it is about to rewrite, so legacy `.dic` files no longer block `ValidateBeforeMigration`.

### Fixed

- `NGramTokeniser` and `EdgeNGramTokeniser` no longer hold a shared `_wordOffsets` list; each span-path call and each `Enumerator` instance now owns its own local list, making the span tokenisation path safe for concurrent use on a shared tokeniser instance.
- `LowercaseFilter` no longer holds a shared `_spanBuffer` field; the span path now rents a buffer from `ArrayPool<char>.Shared` per call and returns it in a `finally` block, eliminating the shared mutable state.
- `Analyser.Clone()` added so `CreateThreadLocalDocumentWriter` can give each DWPT its own `FilteringSpanTokenSink` while sharing the (now stateless) tokeniser and filter references; `IndexWriter.Concurrent` switches on `Analyser` before the fallback arm that shared the original instance across threads.
- Stored binary field reads now return defensive copies so callers cannot mutate cached stored-field buffers.
- `StoredFieldsReader` now validates matching `.fdt` and `.fdx` header versions and block sizes before decoding stored values, and rejects unsupported `.fdt` versions up front.
- `TermInSetQuery` now publishes its cached qualified-term array safely for parallel search execution.
- `FuzzyQuery` now accumulates scores per document so multiple matching term expansions do not inflate hit counts with duplicate documents.
- Hunspell affix parsing now rejects mismatched counted rule lines, applies cross-product suffix conditions to the prefix-modified form, and guards malformed strip lengths.
- Concurrent DWPT merges now preserve per-document binary DocValues and account merged postings for RAM-threshold flushes.

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
- `PostingsEnum` struct copies no longer access freed pooled buffers after the original is disposed; a shared `DisposalGuard` reference detects disposal across all copies. Additional struct fields marked `readonly` to prevent accidental mutation.
- `IndexInput` now has a finaliser that releases the native memory-mapped view pointer when `Dispose` is not called, preventing handle leaks.
- `LiveDocs.Deserialise` catches only `EndOfStreamException` instead of a bare `catch`, and strips orphaned soft-delete timestamps and out-of-range deleted doc IDs from the bitmap for data integrity.
- `SegmentReader` field-less `GetNorm(int)` and `GetFieldLength(int)` overloads removed; all callers already use the field-specific versions. The removed overloads were ambiguous for multi-field indexes.
- `CommitData`, `SegmentInfo`, and `VectorFieldInfo` now validate post-deserialisation invariants; called consistently from `IndexBackup`, `IndexFileInspector`, `IndexRecovery`, and `SegmentInfo.ReadFrom` to catch corrupted index files early.

### Changed

- Wildcard term matching no longer decodes every rejected `body\0m...` candidate into a string.
- `FSTReader` now has a low-allocation offset path for wildcard search, uses ASCII byte matching for
ASCII patterns/terms, and only falls back to string decoding when needed to preserve existing non-ASCII `?` semantics.
