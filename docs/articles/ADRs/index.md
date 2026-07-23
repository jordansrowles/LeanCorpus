# Architecture Decision Records

| ADR | Date | Status | Title |
|-----|------|--------|-------|
| ADR001 | 2026-06-16 | Accepted | [Span-based body encoding for segment serialisation](ADR001-span-body-encoding.md) |
| ADR002 | 2026-06-16 | Accepted | [Single auto-vectorised SIMD path](ADR002-single-simd-path.md) |
| ADR003 | 2026-06-16 | Accepted | [Sorted parallel arrays for HNSW frozen adjacency](ADR003-hnsw-frozen-sorted-arrays.md) |
| ADR004 | 2026-06-16 | Accepted | [ConcurrentDictionary with generation-swap eviction for read-heavy caches](ADR004-concurrentdictionary-cache-pattern.md) |
| ADR005 | 2026-06-16 | Accepted | [Each DWPT flushes its own segment](ADR005-dwpt-segment-flush.md) |
| ADR006 | 2026-06-17 | Accepted | [Defer Stryker.NET mutation testing until upstream bug is fixed](ADR006-stryker-deferred.md) |
| ADR007 | 2026-06-18 | Accepted | [Background merges must never block Commit](ADR007-merge-must-not-block-commit.md) |
| ADR008 | 2026-07-09 | Deprecated | [Streaming codec formats bypass the CodecKit envelope](ADR008-stored-fields-v2-streaming.md) |
| ADR009 | 2026-07-11 | Accepted | [CodecKit trailer format replaces ADR008 custom headers](ADR009-codeckit-trailer-streaming.md) |
| ADR010 | 2026-07-14 | Accepted | [IndexOutput must be disposed before File.Move on Windows](ADR010-close-before-rename-migration.md) |
| ADR011 | 2026-07-21 | Accepted | [Lazy segment readers use bounded leases and process-wide file lifetimes](ADR011-lazy-segment-reader-lifetimes.md) |
## Template

New ADRs should follow [the template](_template.md) using the next available `ADRnnn` prefix.
