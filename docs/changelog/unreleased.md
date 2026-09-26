### Added

- Added optional Shape DocValues `.dvg` v1 for indexed Geo and XY shapes, preserving the exact Packed BKD primitive stream for metadata aggregations.
- Added one-pass heterogeneous numeric and Geo distance, centroid and bounds aggregation requests, including legacy point fallback and complete shape DocValues coverage checks.
- Added bounded invariant 2D Geo and XY WKT parsing and canonical writing, plus explicit deterministic Geo and XY line, polygon and collection simplification.
- Added Geo and XY shape fields and constant-score `Intersects`, `Within`, `Contains` and `Disjoint` queries over stable Packed BKD primitives, including polygon holes, value-aware containment and analytic query circles.
- Added immutable Geo and XY geometry values with `IGeoGeometry` and `IXYGeometry` markers, shared canonical coordinate validation, and a deterministic multidimensional Packed BKD v1 format using new `.pbkd` files.
- Added segment-aware Packed BKD execution for Geo bounding-box and distance queries, with legacy-segment fallback, dateline splitting, and exact Haversine filtering.
- Added `XYPointField` and packed XY bounding-box and distance queries with repeated point DocValues and exact Euclidean filtering.
- Added typed Geo and XY distance sort factories, with origin-aware invariant search-session cursor identities.
- Added DocValues-backed Geo and XY distance sorting with multi-value minima, deterministic ties, missing-value ordering, and SearchAfter/session support.
- Added best-first Packed BKD Top-N for eligible ascending Geo/XY distance sorts, with conservative cell lower bounds, constant-score filter bitmap reuse, and exact fallback scans for legacy Geo segments.

### Changed

- Map Core Search test-source changes to the Search area for affected test runs.
- Apply queued deletes through logical segment members so compound segments can be deleted and updated without unpacking their `.dic` and `.pos` files.
- Set Core package, assembly and file versions to `3.2.0`.
- Use the existing indexed latitude range to select candidates for single-valued packed Geo distance queries, while retaining exact Haversine checks and packed/mixed fallbacks.
- Added construction-time `IndexingConcurrency` configuration and explicit concurrent async bulk ingestion for the Core writer, and made concurrent bulk ingestion use bounded producers through the normal DWPT pipeline.
- Made concurrent indexing ownership explicit for analyser components across maintained built-ins, consolidated automatic DWPT flushing, and now account for active and detached flush-buffer retention separately.
- Detached DWPT flushing now uses bounded background physical execution with ordered writer-owned publication, so indexing producers no longer wait for segment I/O after admission and async ingestion reuses its owning operation lifetime.
- Flush coordination now reserves publication order at submission, applies retained-memory progress backpressure, and drains all accepted physical work before surfacing a detached-flush failure.
- Term flushing now retains qualified terms in UTF-8 through postings metadata and FST construction, avoiding one duplicate managed string per unique term.
- Replaced per-term posting accumulators and production block pools with one pooled `PostingsStore` and sliced byte arena per DWPT, with owned-capacity RAM accounting and deterministic snapshot disposal.
- Streamed postings and FST term bytes directly from detached snapshots, and moved index-sort remapping to one-term pooled scratch while preserving positions, payloads, offsets and term vectors.
- Hardened postings-arena logical read bounds, removed managed metadata per slice, and maintained O(1) owned-capacity posting memory accounting.
- Pooled high-cardinality flush term-ID and posting-offset scratch, and reused the normal positional decode/materialisation pass for term vectors, including index-sorted flushes.
- Completed physical flush execution now releases its detached snapshot graph before ordered publication.
- Hardened Packed BKD v1 reader bounds, semantic validation, deterministic spill cleanup and DWPT capacity accounting without changing the on-disk format.
- Made Packed BKD open read only its tail directory, selected raw leaves on exact prefix-size ties, accounted actual rented build capacity, and added observer-only build and traversal telemetry.
- Renamed spatial configuration and longitude-normalisation APIs to `Point2D()`, `Shape7D4Indexed()` and `NormaliseLongitude()` while preserving the Packed BKD v1 file format.
- `QueryParser.AnalyseTerm` now returns the complete token stream to subclasses; wildcard and range literals reject analyser output that cannot be represented as one term.
- Segment ordinals now use one atomic allocator across detached flush, merge, force-merge, and imported-index paths. Detached flushing also sorts compact term IDs directly against its owned UTF-8 pool rather than allocating per-term byte arrays.
- Reduced repeated `OperationDrain` entry in postings decoding by grouping multi-read decoder work under `BeginReadSession()`, improving representative real-query throughput on Windows and Linux. (c837dbb94, #75)

### Fixed

- Ignore deleted children and parents in block-join search, and drop a whole block during merge when its parent is not retained.
- Keep per-segment deletion generations, live-document counts and soft-delete cutoffs in each commit, so commit publication atomically selects visibility for recovery, snapshots and historical backups.
- Fail closed when selected deletion state is missing, malformed, or inconsistent with segment metadata.
- Expose immutable `SegmentDescriptor` metadata from `SegmentReader.Info` and return deep copies from `GetNrtSegments()`, so later commits cannot change an existing NRT searcher's deletion view.
- Centralise segment file ownership so merge, deletion and recovery cleanup include vector/HNSW and deletion-generation sidecars, while pruning protects active commit and held-snapshot generations.
- Compile query-string clauses with the analyser configured for their selected schema field.
- Preserve query-token escape metadata through wildcard, range, and phrase parsing so escaped metacharacters remain literal.
- Preserve position lengths through legacy filter routing and cached graph replay, with independent cache clones.
- Map character-filter token offsets back to original UTF-16 input across chained transformations.
- Compile phrase token graphs iteratively within explicit traversal and output limits.
- Preserve position lengths and absolute graph edges through common-gram generation and replay.
- Unicode tokenisers recognise supplementary letters and digits while preserving UTF-16 offsets; unpaired surrogates delimit words.
- `StandardAnalyser` now lowercases Unicode tokens invariantly regardless of token length.
- Enforce documented fuzzy-query edit and expansion limits at construction and report invalid parser modifiers at their source offset.
- Preserve exact field lengths above 65,535 and reject negative token counts instead of clamping values.
- Enforce schema, nesting, Boolean clause, wildcard and regexp limits for Server query-string requests before query execution.
- Merges now preserve compatible index sort order while remapping document data, keeping sorted top-N results correct.
- Preserve numeric and Int64 DocValues presence independently of sparse point indexes during segment merges.
- Isolated analyser filter and sink state for concurrent calls and cleaned up execution state after failed analyses.
- Persist only the logical document range in field-length files, excluding unused pooled-array values.
- Lower complete unquoted analyser output with the parser's implicit OR operator and bounded graph-path compilation instead of discarding tokens after the first.
- Best-first Geo nearest sorting now includes legacy-only points in mixed segments after a force merge.
- Made canonical geographic dateline seam normalisation idempotent when a
  canonical polygon or line is used as input again, validated polygon shell and
  hole relationships in one unwrapped world and on the quantised grid, promoted
  XY topology calculations to `double`, and canonicalised signed zero encoding.
- Reduced retained Packed BKD build metadata to one contiguous leaf-data stream
  and stack-based recursive validation scratch.
- Corrected Packed BKD field-name ordering for prefix and Unicode names, and
  rejected incompatible or checksum-corrupt source files before merge rewrites.
- Kept Packed BKD root metadata read-only, documented the separate public 1D
  leaf-size limit, and moved spatial and Packed BKD implementation helpers into
  internal namespaces without changing persisted formats.
- Closed Geo and XY geometry collections to built-in types and clarified the
  Packed BKD namespace boundary.
- Restored the historical public namespace and `Codec.Case`/`Codec.Choice`
  signatures for `CodecKit.Internal.CaseDefinition<TBase>`, retaining only this
  legacy compatibility exception to the public `.Internal` namespace rule.
- Restored merge-throttle backpressure for detached DWPT batches, selected an available DWPT before blocking a concurrent producer, and reconcile active buffered state after a physical-flush failure.
- Prevented detached-flush retained-memory backpressure from spinning when only empty DWPT baseline memory remains, and made ordered publication remove its successful prefix before surfacing a later flush failure.
- Preserve the original detached-flush failure at commit barriers instead of masking it with a generic poisoned-writer exception.
- Return detached DWPT pooled buffers after both successful and failed physical flushes or abort resets, poison physical flush failures at their shared boundary, and reconcile fatal writer state without waiting on admitted-operation leases.
- Poison concurrent indexing admission as soon as a fatal worker failure is observed, while preserving the deterministic lowest-index failure as the reported cause.
- Keep concurrent schema and vector preflight rejection recoverable, account retained token-count capacity, reset retained DWPT maps honestly, and release partially acquired document-block backpressure permits locally.
- Treat every document rejection before DWPT mutation as recoverable, release fatal-reconciliation backpressure before admitted peers unwind, and restore sequence numbering from zero for empty committed indexes.
- Preflight vector dimensions before DWPT mutation and copy accepted vector storage so caller-side array mutation cannot alter buffered or persisted vectors.
- Corrected multi-segment search result merging so Boolean and generic parallel paths preserve exact total-hit counts and global top-N document IDs, including block-max WAND execution. (6925d748d, 2cc3b4fa5, #75)
- Streamed vector segment merges through mapped destination files and bounded quantisation passes, avoiding a second managed float corpus during HNSW rebuilds.
- Preflighted vector merge dimensions and normalisation contracts before writing output, applied the destination writer's quantisation policy across imports and merges, and reused HNSW seeds only when their vector contract matches the destination.
- Cleared pooled stored-field writer scratch before use so previous search activity cannot silently omit fields from newly written segments. (f58ac6c44)
- Released the writer lock when incompatible index metadata aborts `IndexWriter` construction, preventing Windows test-directory cleanup failures. (60b6735ea, #86)
- Synchronised merge-throttling segment inspection with background merge publication without nesting writer and merge locks. (cc3dc2aa5, 34a3c69bd, #86)
- Added structured terminal file-move and cleanup diagnostics, including paths, HRESULT, retry count, and elapsed time. (016a50aa6, #86)
- Fixed merge validation for Shape DocValues component trees, empty spatial aggregations after shape deletion, and WKT output for degenerate Geo and XY rectangles and Geo rectangles spanning longitude -180 to 180.

### Removed

- Removed the disconnected `DocumentBufferState` field-processing and live-DWPT flush paths. All production indexing now detaches owned DWPT batches before segment construction.
- Removed the legacy per-term posting accumulator and `ByteBlockPool`/`IntBlockPool` production paths.

### Deprecated

- Deprecated `InitialiseDwptPool` in favour of `IndexWriterConfig.IndexingConcurrency`, and `AddDocumentLockFree` because it has no distinct lock-free execution path.
### Security

<!--
Only edits to the core libraries get a changelog item.
DevOps, tests, benchmarks, anything else does not.
-->
