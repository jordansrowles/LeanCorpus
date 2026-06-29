# ADR005: Each DWPT flushes its own segment

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

`AddDocumentsConcurrent` built per-thread `DocumentsWriterPerThread` buffers in parallel
via `Parallel.ForEach`, then merged every DWPT into the main `DocumentBufferState` under
`_writeLock` via `MergeDwpt`. The merge phase re-copied every collection: postings with
doc-ID remapping, stored fields, doc token counts, field boosts, numeric index entries,
sorted/numeric/binary doc values with list padding and collection-expression copies, and
vector dictionaries. `AddDocumentLockFree` followed the same pattern.

A `ConcurrentVsSequentialBenchmarks` suite measured the three paths at batch sizes of
100, 1000, and 10 000 documents. The concurrent path was 4 to 36% slower than sequential
`AddDocument` at every size, with 41 to 83% more allocations. The merge tax dominated any
parallelism benefit from the analysis phase.

## Decision

Each DWPT partition flushes its own segment to disk via `SegmentFlusher.FlushFromDwpt`.
No data is merged into `_buffer`. `MergeDwpt`, `MergeMultiValuedDocValues`, and
`AppendMergedStoredField` are deleted. The existing `TieredMergePolicy` consolidates the
resulting segments.

## Rationale

- Doc IDs need no remapping. Each DWPT uses local IDs (0, 1, 2, ...) that become the segment's
  final IDs. The `docBase + localId` arithmetic is eliminated.
- `_writeLock` is not held during the parallel phase. Each DWPT writes to disk independently.
  Only a brief `lock (_writeLock)` protects `_committedSegments.Add` after all segments
  are written.
- The sequential `AddDocument` path is unchanged. `SegmentFlusher.Flush` still operates on
  `DocumentBufferState`. A shared `WritePostingsBody` helper serves both paths.
- Segment count increases proportional to partition count. `TieredMergePolicy` groups
  segments by size tier and merges the smallest when a tier exceeds the threshold.

## Consequences

- `MergeDwpt` (134 lines), `MergeMultiValuedDocValues` (14 lines), and
  `AppendMergedStoredField` (13 lines) are deleted. `ResetDwpt` is deleted.
- `SegmentFlusher.FlushFromDwpt` added. `WritePostingsBody` extracted as a shared helper
  consuming `(string Term, PostingAccumulator Acc)[]` sorted arrays. `WriteNumericIndexDwpt`
  added.
- `AddDocumentsConcurrent` rewritten: each partition calls `SegmentFlusher.FlushFromDwpt`
  directly. `AddDocumentLockFree` rewritten: per-DWPT segment flush on RAM threshold.
  `FlushDwptPool` rewritten: drains remaining DWPT contents as segments during commit.
- `DocumentsWriterPerThread.StoredFieldNameToId` exposed. `ParentDocIds` added (null).
- All 36 DWPT and concurrent integration tests pass on `net10.0` and `net11.0`.
- Throughput is equivalent to the old path. The sequential `AddDocument` remains baseline.
  The primary benefit is maintainability: no double-buffering, no merge phase, no remapping.
- A minimum-document threshold of 128 skips HNSW graph construction on segments where
  brute-force scan is cheaper than graph traversal.
