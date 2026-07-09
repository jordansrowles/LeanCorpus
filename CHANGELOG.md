

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [2.0.0] - Work In Progress

### Added

- v2 stored-fields format that streams compressed blocks directly to disk instead of buffering the entire segment body, eliminating out-of-memory risk on large segments (b69a4c0f)
- StoredFieldsReader.DocCount and StoredFieldsReader.Compression properties exposing metadata from the stored-fields file headers (b69a4c0f)
- `IndexSearcher.Search(Query, SearchOptions)` and `SearchAsync(Query, SearchOptions, CancellationToken)` overloads that honour `StreamResults` for per-segment streaming without a global top-N heap (5573b0c1)
- `IndexOpenGuard` integration tests (877682f5)
- Added `FieldType.Int64` and `Int64Field`, and the pipeline infrastructure (3dfb10a1)
- Added codec constants, format entries, and file extension table entries for `.numl`, `.dvnl`, `.dsnl`, and `.bkdl` (3dfb10a1)
- `ConcurrentVsSequentialBenchmarks` suite comparing `AddDocumentsConcurrent` and `AddDocumentLockFree` throughput against sequential `AddDocument` at batch sizes of 100, 1000, and 10 000 documents. The `concurrent-write` suite name is registered in both `benchmark.ps1` and `benchmark.sh`.
- LINQ queryable provider via `LeanQueryable<T>` and the `LeanExpressionVisitor` expression-tree translator. `Where`, `Select`, `First`, `Single`, `Count`, `Any`, `Take`, `Skip`, `OrderBy`, and `OrderByDescending` operators are supported; lambda predicates (`==`, `!=`, `>`, `>=`, `<`, `<=`, `&&`, `||`, `!`, `.Contains()`, `.StartsWith()`, `.EndsWith()`) are translated into native `Query` objects and executed directly against the index with no intermediate SQL or reflection. The Roslyn source generator emits a zero-allocation field-descriptor switch expression and an `AsQueryable(IndexSearcher)` entry point for `[LeanDocument]`-annotated models. `LeanField<TDoc,TVal>` and `LeanFieldBinding<TDoc>` implement `IFieldDescriptor` for AOT-compatible field resolution. Ships with unit, integration, and chaos test suites.
- Multilingual Wikipedia download script (`scripts/download-wikipedia.sh`) with BCP 47 language code support, jq-based null-delimited record extraction, and exponential backoff rate limiting for 300+ language editions. The PowerShell script (`download-wikipedia.ps1`) gained the same `-Language` parameter for cross-language benchmark data collection.
- LINQ queries tutorial (`docs/tutorials/searching/07-linq-queries.md`) covering quick start, supported operators, LINQ methods, allocation profile, and AOT compatibility guidance.
- Vector quantisation: scalar (float32 → int8) and binary (BBQ) with a `VectorQuantisation` enum (`None`, `Int8`, `BBQ`), configurable per-field via `VectorFieldInfo.Quantisation` and globally via `IndexWriterConfig.VectorQuantisation`. HNSW graphs are built against quantised representations; includes `Int8DistanceComputer` and `BBQDistanceComputer` for distance-aware KNN retrieval, plus an Int8 fast path in HNSW distance computation.
- CodecKit: a composable binary codec framework with primitive codecs (VarInt, VarUInt, fixed-width integers, byte sequences), combinators (fixed-frame, length-prefixed, bytes-owned), integrity wrappers (CRC32, xxHash32, xxHash64 with header or trailer placement, version envelope), Deflate compression wrapping, and an immutable `CodecRegistry`. Ships with unit, integration, chaos, and compression-parity test suites.
- Five new scoring models: `Bm25PlusSimilarity`, `Bm25LSimilarity`, and three TF-IDF variants (`TfIdfAugmentedSimilarity`, `TfIdfDoubleNormSimilarity`, `TfIdfPivotedSimilarity`).
- Three language-model similarities: `LMJelinekMercerSimilarity` (linear interpolation), `DirichletSimilarity` (Bayesian smoothing), and `LMAbsoluteDiscountingSimilarity` (absolute discounting). All consume `CollectionStatistics` for term probability estimation.
- `PostingsHighlighter` and `TermVectorHighlighter` for snippet extraction using stored term-vector offsets without re-analysing the original text. `HybridHighlighter` combines stored-field re-analysis with term-vector snippet placement.
- `StoreDocValues` flag on `StringField`, `TextField`, and `NumericField` (defaults: `true` for `StringField`/`NumericField`, `false` for `TextField`). When `false`, the field skips populating sorted, sorted-set, numeric, and binary DocValues, cutting per-document buffer overhead and shrinking the flush I/O footprint.
- Profiling project with `ActivitySource`-based phased breakdown of indexing time (`add_document`, `analyse`, `flush`, `commit`, `merge`), plus a deletion-phased benchmark.

### Changed

- Test cleanup across 33 files now uses `TestDirectoryFixture.TryDeleteDirectory` with a GC+retry loop instead of silently swallowing `Directory.Delete` failures (6421de1b)
- Highlighter.ExtractTerms now collects terms from WildcardQuery, FuzzyQuery, TermInSetQuery, MultiPhraseQuery, CombinedFieldsQuery, and wrapper queries (ConstantScoreQuery, DisjunctionMaxQuery, FunctionScoreQuery), so highlighting works across more query families (503be7c2)
- IndexCodecMigrator now uses a mandatory staging directory for all migrations, writes migrated segments under new IDs, and atomically publishes the new commit; --in-place flag removed from CLI (d97a9927)
- ICollector is now wired into IndexSearcher via Search(Query, ICollector); Count(Query) returns total hits without materialising a scored heap; CountCollector is now thread-safe (19bf3cdb)
- QueryCache is now owned by SearcherManager and shared across searcher refreshes, so cached results survive when content hasn't changed (7ee5f977)
- FlushDwptPool now asserts that the caller holds WriteLock, making the lock contract explicit and catching misuse in debug builds (84cbc9fc)
- Moved AOT smoke console project into an xUnit 3 AOT test project. (e6741301, 7a79c1b1)
- `ShingleFilter` and `SynonymGraphFilter` now buffer tokens in `Apply` and generate expansions in `Finish` instead of being pass-through stubs.
- `SnowballStemmer` abstract base replaces seven duplicated `RemoveSuffix` implementations with a configurable N-step pipeline, pre/post-processing hooks, and per-language suffix arrays (Italian, Portuguese, Spanish, Dutch, French, German, Russian).
- `SharedStandardIndex` now builds and caches a shared Lucene.NET index alongside the LeanCorpus one, stripping ~500 lines of duplicated Lucene setup/teardown from ten search benchmark suites.
- `IndexWriter.Dispose` drain timeout changed from throwing to logging via `TraceSwallowed`.
- `CombinedFieldsQueryBenchmarks`, `QueryCacheBenchmarks`, and `CollapseAndFacetBenchmarks` use shared `AddDocuments` helpers and pre-built queries to cut duplicated index construction.
- KStemmer rule lookup uses a `FrozenDictionary` keyed by the last two characters of each suffix, with a one-character fallback, replacing linear scan of all rules. `StemTokenFilter` added a character-based pre-filter that skips buffer allocation for tokens whose last character cannot begin a stemming suffix (~85% of tokens).
- Wildcard query execution pre-narrows the FST traversal by walking a known literal prefix (≥2 characters) ahead of the automaton intersection, avoiding per-candidate string materialisation for suffix-only pattern matching. `TermDictionaryReader` gained `GetTermsMatchingWithPrefix` and `GetTermOffsetsMatchingWithPrefix`; `IndexSearcher` and `SegmentReader` plumb through the prefix-narrowed overloads.
- Pending deletion in `IndexWriter` is now applied via a single FST prefix scan per unique field rather than per-term individual lookups, matching against a `HashSet<string>` of bare terms per field. Hard and soft deletes share the same scan infrastructure.
- `Highlighter` detects matching tokens inline during tokenisation via `OffsetCapturingSink`, removing the separate O(n) post-tokenisation match scan and its associated `List<int>` buffer.
- MoreLikeThis candidate selection replaced `List<(float,string,string)>` + `Sort` with a bounded `PriorityQueue` min-heap and a reusable pooled `char[]` buffer for qualified-term construction, eliminating per-term string allocations in the cross-segment DF scan.
- Benchmark temp directories now live under `bench/tmp/` (resolved relative to the repository root) instead of the system temp directory. A new `BenchmarkHelpers` class centralises Create, Delete, and full-tree cleanup; `Program.cs` cleans stale temp directories before and after the full benchmark run. Static Lucene.NET index resources are explicitly cleaned up by each search benchmark class. `CountingTokenSink` is reused as a field across benchmark iterations to avoid skewing allocation measurements.
- Positions in `PostingAccumulator` are now stored as VarInt delta-encoded bytes instead of raw `int[]`, eliminating ~32 MB of GC pressure per 20K-document indexing run.
- The indexing hot path uses an open-addressing byte-ref hash table (`BytesRefHash`) for postings accumulation, removing per-token string allocations during tokenisation.
- Character-offset array allocation in `PostingAccumulator` is gated on `IndexWriterConfig.StoreTermVectors`, reducing per-term allocations by ~5× when term vectors are disabled.
- `ScoreTerm` scoring made branchless via DIM devirtualisation, and phrase queries now intersect candidate documents before decoding positional data.
- SIMD-accelerated ASCII lowercasing added to stemmers, `LowercaseFilter`, and `StandardAnalyser` via `AsciiCharInspector`.
- `Analyser` constructor now accepts `ISpanTokeniser` directly; `IStemmer` removed in favour of `ISpanStemmer`. `AnalyserFactory` provides static construction helpers.
- `RamBufferSizeMB` default raised from 16 to 512.
- `PushDepth` wired into all nesting codecs to enforce a maximum nesting depth, preventing stack overflows on malformed inputs.
- All per-codec format versions reset to 1 following the CodecKit migration. Legacy term dictionary v1/v2 codec, `ICompressionProvider` abstraction, and old-format readers removed.
- Kernel hints (`SequentialScan`, `WriteThrough`) applied to merge I/O paths.
- Concurrent indexing path: each DWPT partition now flushes its own segment to disk via `SegmentFlusher.FlushFromDwpt` instead of merging into the main buffer. `MergeDwpt`, `MergeMultiValuedDocValues`, and `AppendMergedStoredField` are deleted. HNSW graph construction is skipped on segments with fewer than 128 documents. (ADR005)
- `HnswGraph.FromFrozen` no longer allocates a wasted mutable dictionary in the constructor. A private constructor accepts the `frozen` flag and skips mutable-level initialisation, and `_mutableLevels` is no longer `readonly`.
- `IndexWriter.Dispose` no longer spins indefinitely waiting for in-flight indexing operations. A 30-second timeout prevents a stuck `AddDocument` call from hanging process shutdown.
- Every swallowed exception across the library now logs via `LeanCorpusActivitySource.TraceSwallowed`, writing to `Debug.WriteLine` so filesystem errors during cleanup, fsync, merge, and event dispatch are no longer silent.
- `AddDocumentsConcurrent` now clears the DWPT between `Parallel.ForEach` ranges, preventing accumulated document data from inflating segment doc counts when a thread processes multiple ranges. `QueryCache.Put` no longer increments the approximate count on duplicate keys.
- `Commit` now waits for the background merge to finish before returning, preventing a race where a reader opened immediately after commit could see a new commit referencing segment files the merge had not yet flushed to disk.
- `QueryCache` uses `ConcurrentDictionary` with generation-swap eviction instead of `Dictionary`+`Lock`+`LinkedList`. The `TryGet` path is lock-free. `Put` triggers a dictionary swap when the soft entry cap is exceeded. (ADR004)
- `CodecFormatDescriptor` now carries a `HeaderFormat` field per extension, populated from `CodecFormats`, so version checks use the correct codec format rather than a single hardcoded value.
- CodecKit extension points previously marked `internal` are now `public` so third-party codec authors can register custom checksums and formats: `ChecksumAlgorithmId`, `ChecksumPlacement`, `IChecksumProvider`, `CodecFileHeader`, and `CodecFormat`.
- The CodecKit exception hierarchy was reorganised with three purpose-built public base classes. `CodecFormatException` covers structural problems (unknown version, trailing data, insufficient data), `CodecIntegrityException` covers checksum and hash failures, and `CodecResourceException` covers allocation and overflow errors. The intermediate `FormatViolationException` was removed and its subtypes distributed to the new bases. `CodecValidationException` was made public.
- Norms format bumped to v2. `NormsReader` reads both v1 and v2 on open; `NormsWriter` produces v2. A migration path upgrades v1 `.nrm` files in place.
- `SearcherManager.Acquire` and `AcquireLease` spin-waits now throw `TimeoutException` after 30 seconds instead of spinning indefinitely, matching the timeout applied to `IndexWriter.Dispose`.

### Fixed

- Range and Int64 range queries no longer allocate a `HashSet<string>` per document when falling back to stored fields (234ab977)
- `CombinedFieldsQuery.FieldWeights` no longer allocates a new dictionary on every access (944b03f4)
- Prefix, wildcard, fuzzy, range, regex, and automaton term dictionary lookups no longer allocate UTF-8 byte arrays per call (d5f77b6f)
- Duplicate Meter instances with name `Rowles.LeanCorpus` that could cause telemetry listener collisions (4f23137a)
- `IndexOpenGuard` no longer silently skips segments whose codec files cannot be read; IO exceptions and data-corruption errors now propagate so that corrupt or unreadable segment files are detected rather than bypassed as if they were unsupported versions (485fe74a)
- HNSW
  - `QuantisedVectorWriter` no longer quantises every vector twice; packed bytes (Int8) and bit-packed data (BBQ) are stored during the first quantisation pass and written directly in the second, eliminating the duplicate computation pass  (3a0a5ec7)
  - HNSW graph construction for BBQ-quantised vectors now uses Hamming distance (via PopCount on raw bit-packed data) for stored-vs-stored comparisons, matching the query-time distance metric and eliminating the recall degradation caused by optimising the graph for dot-product on dequantised vectors (c1e9344d)
  - HNSW graph construction and search no longer repeatedly dequantises the same stored vectors (c9aeb7ae)
  - `SelectNeighboursHeuristic` now caches dequantised vectors per call, eliminating the inner-loop `O(m * k)` allocations for quantised formats (BBQ, Int8) (c9aeb7ae)
- `NumericDocValuesReader` and `SortedNumericDocValuesReader` now reject `bitsPerValue > 64` and validate packed-bytes length, preventing out-of-bounds reads on malformed doc-value files (7f7525b2)
- `TermVectorHighlighter` now validates the full phrase window instead of only checking immediate neighbours, so phrase highlighting no longer triggers on terms that are adjacent but in the wrong order or from different phrases (087e58d6) 
- Merged segments now inherit the active commit generation instead of being hard-coded to 0, so retention, backup, and migration tooling sees correct metadata (bda24522)
- SegmentMerger.MergeVectors now uses the configured HnswBuildConfig instead of defaults when rebuilding vector graphs during merge (2737b7e9)
- ReverseStringFilter now uses `ArrayPool<char>` for tokens longer than 128 characters instead of unbounded stackalloc, preventing stack overflow on pathologically long tokens (14b5406c)
- IndexBackup.Restore now verifies restored files against manifest CRC-32 checksums after copy, catching hardware errors during restore (98ded332)
- IndexOutputBuffer.Advance(0) no longer discards unflushed buffered bytes by resetting the write position (b03f761b)
- AddIndexes now correctly reads source segment files from the foreign directory instead of the target, fixing cross-directory index import (c24efd41)
- BlockMaxWandScorer
  - now uses per-document norms in block skip entries for tighter upper bounds; deleted documents are correctly excluded from WAND results (52a553c5)
  - now uses LM scoring with proper block-max upper bounds and is wired into the disjunctive query path behind EnableBlockMaxWand (6a3aa769)
  - now scores with proper BM25 and block-max bounds; stateful filters reset between documents and clone correctly for concurrent use (42862187)
- Analyser.Clone() now deep-clones every filter so LanguageAnalyser is safe for concurrent use; stateful filters reset in Finish() and return fresh instances from Clone() (42862187)
- Stateful analysis filters (SynonymGraphFilter, ShingleFilter, LimitTokenCountFilter, CachingTokenFilter) now reset their state between documents, preventing token leakage across multi-document pipelines (94fec8cf)
- PackDelta now validates monotonic non-negative input, catching corrupt postings data before it gets packed (345854c4)
- Phrase and SpanNear queries now score as a sum of all term weights rather than using only the rarest term (fed34fc8)
- `StemTokenFilter` no longer skips stemming based on an English-centric suffix-letter pre-filter, fixing incomplete stems for non-English languages (ee28d923)
- `WordDelimiterFilter` and `UniqueTokenFilter` are now fully implemented and tested (10fac633)
- `JsonDocumentMapper`/`JsonMappingOptions` fixes (3dfb10a1)
  - Depth overflow now throws instead of silently dropping data
  - `JsonMappingOptions` validates `MaxDepth` and `StringFieldMaxLength`
  - JSON strings now default to `StringField`
- `ForceMerge` now cleans up segment files for merged segments, matching the behaviour of Compact and preventing unbounded disk growth (643a349f)
- `Compact`, `ForceMerge`, and background merges now refuse to run while a two-phase prepared commit is pending, preventing commit-generation collisions that would throw `IOException` on publish (ebadf8d1)
- `AddDocumentsConcurrent` now flushes segments under `WriteLock` so concurrent deletes and commits cannot interleave between segment creation and visibility (90e7a528)
- `ExecuteFilterToBitmap` no longer mutates the shared `SegmentReader.DocBase`. Now subtracts the captured base from collected global IDs, eliminating a data race on concurrent vector-filter searches (b4488e0e)
- `NumericAggregator` no longer probes only docId 0 to resolve field type; now checks field existence across all three numeric stores, fixing silent zero results when the field was absent from the first document (ef8c9033)
- Boolean index scoring fallback no longer inflates legitimate zero scores to 1.0f (ffd46248)
- `MoreLikeThis` now excludes the source document. (6974ebda)
- Properly wired `IndexOfNormalisableDigit`/`NormaliseDigit` helpers into `Apply`. Add several tests. Updated `AnalysisSmokeTests` to assert actual normalised values rather than just token count. (1cd3010b, c0f1192d)
- `Compact()`, `WriteNorms()`, `PushDepth`, and `CodecFormat` validation bugs caught by codec audit.
- Double-byte-copy in `AddBinaryDocValue`: the string overload encoded to UTF-8 then called the span overload which called `ToArray()` a second time. Both paths now route through a shared core method, allocating once.
- AOT smoke test script now auto-detects the OS when selecting the runtime identifier.
- Highlighter and similarity benchmark comparisons against Lucene.NET corrected.
- Every `await` in the library now includes `ConfigureAwait(false)`, preventing continuations from capturing the caller's `SynchronizationContext`.
- `GetVector(int docId)` in `SegmentReader.DocValues` now iterates all vector fields instead of returning on the first iteration, fixing a bug where only one field was ever checked.
- `NumericDocValues` bit-packing range calculation uses signed subtraction to keep the compiler happy on unsigned underflow in unchecked arithmetic.
- `IndexOutputBuffer` now implements `IDisposable`; callers use `using` blocks to return rented arrays to the pool.
- `posix_fadvise` P/Invoke accepts a `SafeFileHandle` directly instead of unsafely extracting the raw fd.
- `BackpressureController.AcquireBackpressureSlotAsync` uses `WaitAsync` instead of a blocking `Wait(0)` on the async path.
- `IndexBackup` retry loops converted from `for(;;)` to `while(true)` so the stop condition and incrementer stay close to the exit check.
- AOT example file-scoped types moved into a named namespace.
- Floating-point equality on `RamBufferSizeMB` and scoring boosts replaced with range comparisons.
- Generic null checks across CodecKit (`CaseDefinition`, `ChoiceCodec`, `OptionalCodec`, `VersionedCodec`, `VersionEnvelopeCodec`) switched to `is null` so the compiler's nullable analysis stays accurate.
- Benchmark regex instances include a timeout, silencing ReDoS hotspot noise.

### Removed

- CodecFormats.StoredFields static field — the stored-fields format no longer uses the CodecKit envelope and writes headers directly through StoredFieldsFileHeader (b69a4c0f)
- Dead `NumericFields` buffer (`List<Dictionary<string, double>>`) from `DocumentBufferState` and `IndexWriter.FieldProcessing`; it allocated an empty dictionary for every document containing a numeric field but was never read, while the actual numeric index lives in `NumericIndex` (614846ad)

## [1.4.1] - 2026-06-13

### Added

- `GetStoredFields` overload with an optional `fieldsToLoad` parameter for selective stored-field retrieval, reducing allocations when only a subset of fields is needed.

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
