### Added

### Changed

### Fixed

- Cleared pooled stored-field writer scratch before use so previous search activity cannot silently omit fields from newly written segments.
- Released the writer lock when incompatible index metadata aborts `IndexWriter` construction, preventing Windows test-directory cleanup failures.
- Synchronised merge-throttling segment inspection with background merge publication and made background-refresh coverage scheduler-friendly under stress execution.
- Added structured terminal file-move and cleanup diagnostics, including paths, HRESULT, retry count, and elapsed time, and deterministically disposed the merge regression directory.

### Removed

### Deprecated

### Security

<!--
Only edits to the core libraries get a changelog item.
DevOps, tests, benchmarks, anything else does not.
-->
