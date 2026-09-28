---
adr: ADR011
title: Lazy segment readers use bounded leases and process-wide file lifetimes
date: 2026-07-21
status: Accepted
version-added: 2.1.0
summary: Use bounded lazy segment readers with shared file lifetimes.
areas: [search, store, indexing, concurrency]
---

# ADR011: Lazy segment readers use bounded leases and process-wide file lifetimes

- **Date:** 2026-07-21
- **Status:** Accepted

## Context

`IndexSearcher` previously constructed a complete `SegmentReader` for every
committed segment. Opening 2,700 unmerged Linux-kernel segments took 77.888
seconds and left a 2,905 MB process working set before the first query. The
reader opened term dictionaries, postings, stored fields, norms, field lengths,
deletions, DocValues, and numeric indexes during construction.

Deferring those components creates a file-lifetime problem. A writer may commit
a merge and request deletion of the old files while an older searcher still
needs to open one of them. The existing mapped-input counts belonged to one
`MMapDirectory`, so a writer and searcher using separate directory instances did
not share deletion state.

## Decision

`SegmentReader` is a metadata facade. Direct instances create their heavy state
on first use and retain it privately. An `IndexSearcher` gives all of its facades
one thread-safe LRU cache, bounded by
`IndexSearcherConfig.MaxCachedSegmentReaders`, which defaults to 256, and
`IndexSearcherConfig.MaxCachedSegmentReaderBytes`, which defaults to 256 MiB.
The byte limit weights each cached state by logical mapped-file lengths and
estimated materialised arrays, grouped by reader component and exposed through
`IndexSearcher.SegmentReaderCacheMetrics`. These figures estimate retained
reader resources; they do not represent process working set or RSS.

Cache hits return value-type leases. An entry with an active lease cannot be
evicted. Concurrent operations and cursors may temporarily take the cache over
either bound. Releasing the last lease refreshes the resource estimate and
trims inactive entries by both count and estimated bytes. There is no
segment-count rule that makes warmed heavy states permanently resident. The
metadata facade remains available while its heavy state is evicted and can be
loaded again. Loading occurs outside the cache lock, and concurrent first
access runs one factory. Evicted values are disposed after leaving the cache
lock.

Every top-level segment query holds a lease for the complete operation. A
returned `PostingsEnum` transfers its lease to the cursor's existing shared
disposal guard. This keeps copied cursors, mapped postings, vector readers, and
HNSW vector sources valid until their operation ends.

Each vector field owns one lazy holder for its vector reader, quantised reader,
and HNSW graph. First access to a field is synchronised by that holder, so
initialising one field does not block another field. Successful readers and
graphs are published once and reused; a missing graph is cached, while failed
opens or graph reads remain retryable. Segment state disposes each field's
graph before its vector reader after active operation leases have drained.

Committed segment files are protected by one searcher snapshot lease acquired
from a single directory inventory. A process-wide registry, keyed by canonical
directory and concrete file path, coordinates snapshots, mapped inputs, and
deletion across separate `MMapDirectory` instances. Deletion remains pending
until the final lease is released. Failed snapshot or mapped-input acquisition
does not retain a count.

Opening a searcher still validates the commit, migration markers, segment
metadata, and required logical file presence. Direct `SegmentReader`
construction and explicit segment-list searchers use the same structural check
as committed-index recovery, including metadata-declared vector and HNSW files
for both loose and compound segments. Individual codec headers and corruption
checks occur when their component is first loaded. Writer compatibility checks
remain eager so a writer cannot append to an index that needs migration.
Persisted `IndexStats` load normally. When they are absent, the segment scan is
deferred until `Stats` or scoring first needs it.

DocValues are genuinely on demand again. Snapshot leases replace the eager
workaround that was introduced to avoid a merge deletion race.

## Consequences

- Searcher construction scales with compact segment metadata rather than FSTs,
  postings mappings, norms, stored fields, and DocValues.
- `MaxCachedSegmentReaders` remains a secondary entry-count limit. The byte
  budget evicts heavier states first by LRU order, while the public metrics show
  retained estimates by component. A component remains owned by its state and
  is disposed with that state after active leases end.
- Resource estimates use logical compound-member lengths, so accounting does
  not charge every reader for the whole `.cfs` file. Array estimates include
  loaded materialisations; they are conservative guidance for retained cache
  resources, not an exact managed-heap or mapped-page measurement.
- Vector and HNSW first-touch state is isolated per field; one field's cold
  reader or graph load does not serialise other fields. All resources remain
  owned by the segment state and follow its lease-protected disposal lifetime.
- Smaller budgets can increase reader reload and DocValues materialisation
  work. A configured entry count no longer pins all warmed readers when it is
  greater than the active segment count.
- Old searchers remain valid while another directory instance merges and cleans
  up their segments. Obsolete files are removed after all snapshots and mappings
  close.
- This decision does not add merge controls, `SearcherManager` reader reuse, or
  mapped FST representations.
