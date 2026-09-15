### Added

### Changed

- Added an opt-in net11.0 Runtime Async build switch for the Core and Community Server packages. Default builds remain Runtime Async off while the LEAN-16 compatibility and performance spike records comparable artefacts.
- Added construction-time `IndexingConcurrency` configuration and explicit concurrent async bulk ingestion for the Core writer, and made concurrent bulk ingestion use bounded producers through the normal DWPT pipeline.
- Made concurrent indexing ownership explicit for analyser components across maintained built-ins, consolidated automatic DWPT flushing, and now account for active and detached flush-buffer retention separately.
- Detached DWPT flushing now uses bounded background physical execution with ordered writer-owned publication, so indexing producers no longer wait for segment I/O after admission and async ingestion reuses its owning operation lifetime.
- Flush coordination now reserves publication order at submission, applies retained-memory progress backpressure, and drains all accepted physical work before surfacing a detached-flush failure.
- Term flushing now retains qualified terms in UTF-8 through postings metadata and FST construction, avoiding one duplicate managed string per unique term.
- Segment ordinals now use one atomic allocator across detached flush, merge, force-merge, and imported-index paths. Detached flushing also sorts compact term IDs directly against its owned UTF-8 pool rather than allocating per-term byte arrays.
- Reduced repeated `OperationDrain` entry in postings decoding by grouping multi-read decoder work under `BeginReadSession()`, improving representative real-query throughput on Windows and Linux. (c837dbb94, #75)

### Fixed

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

### Deprecated

- Deprecated `InitialiseDwptPool` in favour of `IndexWriterConfig.IndexingConcurrency`, and `AddDocumentLockFree` because it has no distinct lock-free execution path.
### Security

<!--
Only edits to the core libraries get a changelog item.
DevOps, tests, benchmarks, anything else does not.
-->
