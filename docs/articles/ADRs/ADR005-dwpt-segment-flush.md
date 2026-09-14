---
adr: ADR005
title: Each DWPT flushes its own segment
date: 2026-06-16
status: Accepted
version-added: 2.0.0
summary: Flush each document-writing thread directly to its own segment.
areas: [indexing, merging, concurrency]
---

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

Each DWPT detaches an owned `DwptFlushBatch` that becomes one segment through the
writer-owned bounded `FlushCoordinator`. No data is merged into a shared document
buffer. Physical segment construction is independent bounded work and the coordinator
publishes completed `SegmentInfo` instances in submission order. The existing
`TieredMergePolicy` consolidates the resulting segments.

## Rationale

- Doc IDs need no remapping. Each DWPT uses local IDs (0, 1, 2, ...) that become the segment's
  final IDs. The `docBase + localId` arithmetic is eliminated.
- `_writeLock` is not held during analysis or physical segment construction. The
  coordinator owns detached batches until terminal success or failure, then takes the
  writer lock only to publish the completed ordered prefix.
- Ordinary and concurrent ingestion use the same DWPT pool. `SegmentFlusher.FlushFromBatch`
  consumes the exclusively owned detached batch directly; there is no shared
  `DocumentBufferState`, snapshot view, or secondary flush source.
- Segment count increases proportional to partition count. `TieredMergePolicy` groups
  segments by size tier and merges the smallest when a tier exceeds the threshold.

## Consequences

- `MergeDwpt` (134 lines), `MergeMultiValuedDocValues` (14 lines), and
  `AppendMergedStoredField` (13 lines) are deleted. `ResetDwpt` is deleted.
- `DwptFlushBatch` owns all detached resources, including rented term and posting buffers,
  until one-shot cleanup. `SegmentFlusher.FlushFromBatch` is the only production flush
  entry point. The postings writer and FST sort compact term IDs against the batch's UTF-8
  term pool, without allocating per-term byte arrays or managed term strings.
- `AddDocumentsConcurrent`, ordinary `AddDocument`, and concurrent async ingestion use the
  same DWPT and coordinator pipeline. `FlushDwptPool` submits remaining owned batches and
  commit, mutation, merge, snapshot, and dispose boundaries drain the required work.
- `DocumentsWriterPerThread.StoredFieldNameToId` exposed. `ParentDocIds` added (null).
- Flush execution is bounded by `MaxConcurrentFlushes`; accepted detached work reaches a
  terminal success or writer-poisoning failure even when shutdown begins.
- Throughput is equivalent to the old path. The sequential `AddDocument` remains baseline.
  The primary benefit is maintainability: no double-buffering, no merge phase, no remapping.
- A minimum-document threshold of 128 skips HNSW graph construction on segments where
  brute-force scan is cheaper than graph traversal.
