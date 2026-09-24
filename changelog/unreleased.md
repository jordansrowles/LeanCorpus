### Added

- Added Geo and XY shape fields and constant-score `Intersects`, `Within`, `Contains` and `Disjoint` queries over stable Packed BKD primitives, including polygon holes, value-aware containment and analytic query circles.
- Added immutable Geo and XY geometry values with `IGeoGeometry` and `IXYGeometry` markers, shared canonical coordinate validation, and a deterministic multidimensional Packed BKD v1 format using new `.pbkd` files.
- Added segment-aware Packed BKD execution for Geo bounding-box and distance queries, with legacy-segment fallback, dateline splitting, and exact Haversine filtering.
- Added `XYPointField` and packed XY bounding-box and distance queries with repeated point DocValues and exact Euclidean filtering.
- Added typed Geo and XY distance sort factories, with origin-aware invariant search-session cursor identities.
- Added DocValues-backed Geo and XY distance sorting with multi-value minima, deterministic ties, missing-value ordering, and SearchAfter/session support.
- Added best-first Packed BKD Top-N for eligible ascending Geo/XY distance sorts, with conservative cell lower bounds, constant-score filter bitmap reuse, and exact fallback scans for legacy Geo segments.
- Added a spatial nearest benchmark matrix comparing exact sorting with best-first Geo/XY Top-N across uniform, clustered and multi-value points, Top-N sizes and filter selectivity, recording Packed BKD traversal counters and packed-only versus mixed legacy/packed Geo overhead.

### Changed

- Apply queued deletes through logical segment members so compound segments can be deleted and updated without unpacking their `.dic` and `.pos` files.
- Set Core package, assembly and file versions to `3.2.0` for the Sprint 2 release.
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
- Completed physical flush execution now releases its detached snapshot graph before ordered publication, while deterministic writer lifecycle coverage removes throughput-sensitive shutdown coordination tests.
- Hardened Packed BKD v1 reader bounds, semantic validation, deterministic spill cleanup and DWPT capacity accounting without changing the on-disk format.
- Made Packed BKD open read only its tail directory, selected raw leaves on exact prefix-size ties, accounted actual rented build capacity, and added observer-only build and traversal telemetry.
- Renamed spatial configuration and longitude-normalisation APIs to `Point2D()`, `Shape7D4Indexed()` and `NormaliseLongitude()` while preserving the Packed BKD v1 file format.
- Made DevOps managed test target resolution disable MSBuild servers and use single-node evaluation, while pinning the selected SDK host for task-host reliability.
- Segment ordinals now use one atomic allocator across detached flush, merge, force-merge, and imported-index paths. Detached flushing also sorts compact term IDs directly against its owned UTF-8 pool rather than allocating per-term byte arrays.
- Reduced repeated `OperationDrain` entry in postings decoding by grouping multi-read decoder work under `BeginReadSession()`, improving representative real-query throughput on Windows and Linux. (c837dbb94, #75)

### Fixed

- Best-first Geo nearest sorting now includes legacy-only points in mixed segments after a force merge.
- Made canonical geographic dateline seam normalisation idempotent when a
  canonical polygon or line is used as input again, validated polygon shell and
  hole relationships in one unwrapped world and on the quantised grid, promoted
  XY topology calculations to `double`, and canonicalised signed zero encoding.
- Reduced retained Packed BKD build metadata to one contiguous leaf-data stream
  and stack-based recursive validation scratch, while expanding generated
  lifecycle and corruption coverage.
- Added a replayable Geo/XY state-machine model, NRT snapshot coverage,
  interrupted packed-point flush recovery, mixed legacy fallback with a corrupt
  packed segment, and nearest-search corruption/cancellation cleanup coverage.
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
- Cleared pooled stored-field writer scratch before use so previous search activity cannot silently omit fields from newly written segments. (f58ac6c44)
- Released the writer lock when incompatible index metadata aborts `IndexWriter` construction, preventing Windows test-directory cleanup failures. (60b6735ea, #86)
- Synchronised merge-throttling segment inspection with background merge publication without nesting writer and merge locks, and made background-refresh coverage scheduler-friendly under stress execution. (cc3dc2aa5, 34a3c69bd, #86)
- Added structured terminal file-move and cleanup diagnostics, including paths, HRESULT, retry count, and elapsed time, and deterministically disposed the merge regression directory. (016a50aa6, #86)

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
