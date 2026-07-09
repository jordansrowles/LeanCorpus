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
| ADR008 | 2026-07-09 | Accepted | [Stored fields v2 streams outside the CodecKit envelope](ADR008-stored-fields-v2-streaming.md) |

## Template

New ADRs should follow [the template](_template.md) using the next available `ADRnnn` prefix.
