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
| ADR012 | 2026-07-24 | Accepted | [Parallel segment search is opt-in](ADR012-parallel-search-opt-in.md) |
| ADR013 | 2026-07-27 | Accepted | [Custom queries extend the tuned execution pipeline](ADR013-query-extension-pipeline.md) |
| ADR014 | 2026-07-27 | Accepted | [Japanese dictionaries use a LeanCorpus language codec](ADR014-japanese-language-codec.md) |
| ADR015 | 2026-07-27 | Accepted | [Pagination and rescoring use bounded collector strategies](ADR015-bounded-second-stage-search.md) |
## Template

New ADRs should follow [the template](_template.md) using the next available `ADRnnn` prefix.

## Reasons for an ADR
If at any point during work an ADR is deserved, create one. But only if the reason fulfills ones of these:
- Is costly to reverse
- Trade-off heavy (there were real alternatives, each with pros/cons, and you picked one over another for specific reasons)
- Cross-cutting (it constrains how other parts of the system get built)
- Non-obvious solutions
- Major changes in any of the following areas: index structure, storage formats, analyser/tokeniser pipeline desings, concurrency and consistency model, segment merging, scoring/ranking, query parsing, low-level designs
