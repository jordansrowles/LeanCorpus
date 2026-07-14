# ADR007: Background merges must never block Commit

- **Date:** 2026-06-18
- **Status:** Accepted

## Context

Commit `cce9af27` added a synchronous wait inside `Commit()` that blocks until the
background merge finishes. The intent was to prevent a reader race where an
`IndexSearcher` opens a commit point whose segment files are being deleted by a
concurrent merge, which can fault on Windows.

The cost was immediate and severe: on 100K documents with `MaxBufferedDocs=10,000`,
the merge consolidates ~10 segments and adds 4.8 seconds and 2.2 GB of allocation to
every commit. This regressed the indexing benchmark from 11.1 s / 1.32 GB to
16.7 s / 3.91 GB. Lucene.NET baselines were unaffected.

## Decision

`Commit()` returns immediately after the commit file is written. The background merge
runs asynchronously via `Task.Factory.StartNew`, exactly as it did at `50d0c68b`.
No caller waits for the merge.

The merge logic was already extracted from `ScheduleBackgroundMerge` during the fix.
It is now invoked synchronously from `IndexWriter.ThrottleMerge` by scheduling
a background merge and waiting for it.

The five heap-allocated state wrapper objects (`CommitState`, `BackpressureState`,
`MergeState`, `SnapshotState`, `DwptState`) introduced in `213b48a1` were also
removed and their fields inlined back into `IndexWriter`. Manager methods now take
`IndexWriter writer` directly instead of 5--13 separate parameters. These changes
recovered ~0.13 s and ~0.09 GB beyond the baseline.

## Rationale

The merge is not required for correctness on Linux, which is both the benchmark and
production host. Linux `mmap` + `unlink` keeps the file alive until the last mapping
is released; a reader with an open `mmap` on a segment file is unaffected when the
merge deletes that file. The race `cce9af27` wanted to close is a Windows concern
that should be solved at the `MMapDirectory` level (reference-counted file cleanup),
not by forcing every `Commit` to wait for merge I/O.

The commit that added the wait was one of ~30 in a batch between two working
baselines. The wait was not present in the previous baseline (`50d0c68b`) and the
system operated without it for the entire development cycle.

## Consequences

- `CommitWithLocks` no longer waits for `_mergeTask`. The 10-line merge-wait block
  is deleted.
- `CommitCore` calls `ScheduleBackgroundMerge` (async).
- `IndexWriter.ThrottleMerge` schedules a background merge and blocks until it
  completes, used by `AddDocument`/`AddDocumentAsync` when
  `MergeThrottleSegments` is configured.
- A reader that opens an index while a background merge is deleting segment files
  may encounter `FileNotFoundException` on Windows. This is a pre-existing condition
  that `cce9af27` attempted but failed to fix properly. A proper cross-platform fix
  (segment reference counting in `MMapDirectory`) is deferred.
- `MergeScheduler.MergeIfNeeded` was removed in favour of `ScheduleBackgroundMerge`
  plus `Task.Wait` in `IndexWriter.ThrottleMerge`, avoiding duplicated merge logic.
- The five state wrapper classes (`CommitState`, `BackpressureState`, `MergeState`,
  `SnapshotState`, `DwptState`) are deleted. Manager classes access `IndexWriter`
  fields directly through a single `IndexWriter writer` parameter.
