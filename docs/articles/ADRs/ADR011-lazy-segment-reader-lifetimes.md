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
`IndexSearcherConfig.MaxCachedSegmentReaders`, which defaults to 256.

Cache hits return value-type leases. An entry with an active lease cannot be
evicted. Concurrent operations may temporarily take the cache over capacity,
but releasing a lease trims inactive entries back to the configured bound.
Loading occurs outside the cache lock, and concurrent first access runs one
factory. Evicted values are disposed after leaving the cache lock.

Every top-level segment query holds a lease for the complete operation. A
returned `PostingsEnum` transfers its lease to the cursor's existing shared
disposal guard. This keeps copied cursors, mapped postings, vector readers, and
HNSW vector sources valid until their operation ends.

Committed segment files are protected by one searcher snapshot lease acquired
from a single directory inventory. A process-wide registry, keyed by canonical
directory and concrete file path, coordinates snapshots, mapped inputs, and
deletion across separate `MMapDirectory` instances. Deletion remains pending
until the final lease is released. Failed snapshot or mapped-input acquisition
does not retain a count.

Opening a searcher still validates the commit, migration markers, segment
metadata, and required file presence. Individual codec headers and corruption
checks occur when their component is first loaded. Writer compatibility checks
remain eager so a writer cannot append to an index that needs migration.
Persisted `IndexStats` load normally. When they are absent, the segment scan is
deferred until `Stats` or scoring first needs it.

DocValues are genuinely on demand again. Snapshot leases replace the eager
workaround that was introduced to avoid a merge deletion race.

## Consequences

- Searcher construction scales with compact segment metadata rather than FSTs,
  postings mappings, norms, stored fields, and DocValues.
- Indexes with more active segments than the cache capacity trade bounded memory
  for reload work. Broad queries may reload readers with the default capacity.
- A cache sized to at least the segment count retains all warmed readers and is
  intended for workloads that prioritise repeat-query latency over memory.
- Old searchers remain valid while another directory instance merges and cleans
  up their segments. Obsolete files are removed after all snapshots and mappings
  close.
- This decision does not add merge controls, `SearcherManager` reader reuse, or
  mapped FST representations.
