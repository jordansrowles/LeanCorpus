### Added

### Changed

- Added construction-time `IndexingConcurrency` configuration and explicit concurrent async bulk ingestion for the Core writer, and made concurrent bulk ingestion use bounded producers through the normal DWPT pipeline.
- Made concurrent indexing ownership explicit for analyser components across maintained built-ins, consolidated automatic DWPT flushing, and now account for active and detached flush-buffer retention separately.
- Reduced repeated `OperationDrain` entry in postings decoding by grouping multi-read decoder work under `BeginReadSession()`, improving representative real-query throughput on Windows and Linux. (c837dbb94, #75)

### Fixed

- Return detached DWPT pooled buffers after both successful and failed physical flushes, enforce the configured physical-flush limit, and defer concurrent-batch abort until producers have unwound.
- Preflight vector dimensions before DWPT mutation and copy accepted vector storage so caller-side array mutation cannot alter buffered or persisted vectors.
- Corrected multi-segment search result merging so Boolean and generic parallel paths preserve exact total-hit counts and global top-N document IDs, including block-max WAND execution. (6925d748d, 2cc3b4fa5, #75)
- Cleared pooled stored-field writer scratch before use so previous search activity cannot silently omit fields from newly written segments. (f58ac6c44)
- Released the writer lock when incompatible index metadata aborts `IndexWriter` construction, preventing Windows test-directory cleanup failures. (60b6735ea, #86)
- Synchronised merge-throttling segment inspection with background merge publication without nesting writer and merge locks, and made background-refresh coverage scheduler-friendly under stress execution. (cc3dc2aa5, 34a3c69bd, #86)
- Added structured terminal file-move and cleanup diagnostics, including paths, HRESULT, retry count, and elapsed time, and deterministically disposed the merge regression directory. (016a50aa6, #86)

### Removed

### Deprecated

- Deprecated `InitialiseDwptPool` in favour of `IndexWriterConfig.IndexingConcurrency`, and `AddDocumentLockFree` because it has no distinct lock-free execution path.
### Security

<!--
Only edits to the core libraries get a changelog item.
DevOps, tests, benchmarks, anything else does not.
-->
