# ADR003: Sorted parallel arrays for HNSW frozen adjacency

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

The HNSW graph's frozen (read-only) form used a `List<Dictionary<int, int[]>>`
where each level held a dictionary mapping document IDs to neighbour arrays.
Dictionary lookups on the query hot path incur hash computation and pointer
chasing through bucket chains, and the per-level dictionaries have poor
cache locality when iterating all nodes at a level during graph traversal.

## Decision

Replace `Dictionary<int, int[]>` with a `FrozenLevel` type that stores two
parallel arrays sorted by document ID: `int[] _sortedDocIds` and
`int[][] _neighbourArrays`. Lookup uses `Array.BinarySearch` on the sorted
IDs followed by direct array indexing.

## Rationale

The frozen graph is built once during `Freeze()` or deserialisation and
read many times during search. The sort cost is paid at freeze time.
Binary search on a sorted `int[]` is competitive with dictionary probing
for the typical node counts at each HNSW level. The parallel arrays sit
in contiguous memory, giving the CPU prefetcher an opportunity to stream
neighbour lists during greedy descent. The design also removes hash-table
overhead (bucket arrays, linked entries) from the search path entirely.

## Consequences

- `FrozenLevel` class added to `HnswGraph.cs` with `FromMutable()` and
  `ToDictionary()` factory methods.
- `HnswReader` builds `FrozenLevel` directly from the `.hnsw` file.
- `HnswWriter`, `Freeze()`, `Thaw()`, `GetNeighbours()`, `GetNodesAtLevel()`,
  `ContainsNode()`, and `NeighboursAt()` updated to use the new type.
- The mutable form (`List<Dictionary<int, List<int>>>`) is unchanged;
  only the frozen representation was altered.
- Any future immutable spatial or graph index in LeanCorpus should follow
  the same pattern of sorting at build time for cache-friendly reads.
