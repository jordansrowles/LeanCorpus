---
adr: ADR052
title: "Opt-in Float32 vector graph"
date: 2026-11-22
status: Accepted
last-updated: 2026-11-23
version-added: v4.0.0
summary: "Restore opt-in approximate search for Float32 vectors through a new rebuildable .vgr graph role that selects candidates for exact rescoring, with explicit exact fallbacks and a reference-parity qualification gate."
areas: [vectors, codecs, hnsw, indexing, search]
relates-to: [ADR003, ADR025, ADR039, ADR042, ADR049]
---

# ADR052: Opt-in Float32 vector graph

- **Date:** 2026-11-22
- **Status:** Accepted

## Context

ADR042 retired new HNSW writes for 4.0 after the frozen Uniform matrix failed:
the unchanged EF64 default passed `0/8` groups and EF400 still failed both 768D
groups while visiting about 70% of nodes. Its reintroduction criteria require a
new accepted ADR before any graph writer returns.

A Python port of the retired Float32 graph, compared with hnswlib at M=16,
efConstruction=100 and K=10, matched or beat the reference on every case,
including Uniform 768D (port 0.895, hnswlib 0.834 recall@10 at EF400). Both need
most of the graph on uniform high-dimensional data, which has no navigable
neighbourhood structure. The NearDuplicate groups collapse to identical vectors
after normalisation, so literal-ID recall is capped near 0.70 even for an
exhaustive scan, while tie-aware recall is 1.000. ADR042 criterion 1 (recall at
least 0.90 on Uniform 768D) and criterion 5 (material benefit over exact search)
therefore cannot both hold on that matrix for any HNSW implementation. The
failures were a contract and performance problem, not a demonstrated traversal
defect. Three further problems blocked a writer:

- with `NormaliseVectors=false` the retired graph ranked by raw dot product and
  reranked by cosine, the mismatch that sank Int8;
- `.hnsw` v1 cannot be bumped: historical BBQ search traverses it, Int8 v1
  presence reconstruction depends on its membership, and a newer current version
  would make every older graph block index writes;
- the hot path used dictionaries, hash sets, LINQ and a binary search per
  neighbour lookup.

The owner decided on 12 November 2026 that 4.0 may ship Float32 graphs alone
(Int8 and BBQ stay retired), that reference parity at equal work supersedes the
frozen Uniform gate for HNSW, and that heavy qualification runs separately.

## Decision

**Scope.** `IndexWriterConfig.BuildHnswOnFlush` becomes valid again for
unquantised (`VectorQuantisation.None`) fields and keeps its `false` default.
Int8 and BBQ creation remain retired exactly as ADR042 records; a graph never
makes them valid. Historical `.hnsw` v1 graphs remain ignored by Float32 and
Int8 queries.

**Metric contract.** The public metric is cosine similarity, and graph
construction, traversal and final scoring all target it. Normalise-at-write stays
the canonical stored form when `NormaliseVectors` is true. Otherwise stored values
are the supplied values and the graph persists one inverse L2 norm per node.
For a node whose norm lies in `[2^-50, 2^50]`, query-to-node distance is
`-dot(q, v) * invNorm(v)` for a unit query and node-to-node distance is
`-dot(a, b) * invNorm(a) * invNorm(b)`; in that range no Float32 product can
overflow or lose the result to underflow. Any other non-zero vector, such as
`[1e20, 0]` or a subnormal `[1e-40, 0]`, persists the marker `-1` and every
distance involving it uses the exact scorer's cosine, which rescales in double
precision. A zero vector has inverse norm zero, so its distance is zero,
matching the exact scorer. Construction and traversal therefore rank every
finite input as the exact scorer does; rescoring is not relied on to correct
candidate selection. Graph distances never reach the collector: every candidate is rescored by the exact
scorer over the stored Float32 vector, and query and field boosts apply after
cosine as before. The route is purely a candidate-selection choice.

**Persisted identity.** A new catalogue role, not `.hnsw` v2:

| Property | Value |
| --- | --- |
| Format ID | `leancorpus.vectors.graph` |
| Family | `leancorpus.vectors` |
| Extension | `.vgr`, per field `{segment}_v_{sanitisedField}.vgr` |
| Body version | 1, canonical frame, XxHash64, random access |
| Migration | `None`; a graph is a rebuildable accelerator with no route from `.hnsw` |
| Segment metadata | `VectorFieldInfo.HasGraph` (default `false`); `HasHnsw` keeps its historical meaning |

The body stores the dimension, a scorer byte (1 = Float32 cosine; any other
value fails closed), flags (normalised, inverse norms present), M, M0,
efConstruction, seed, node count, maximum level and entry ordinal, then the
ascending document IDs of the ordinal nodes, a fixed-stride `1 + M0` level-zero
block, each upper level's ascending ordinals and `1 + M` records, and the inverse
norms when present (`0` for a zero vector, `-1` for a vector outside the
conditioned range). Unused slots are `-1`. The reader validates every structural
rule before exposing a graph: field dimension and normalisation, parameter
ranges, strictly ascending document IDs that are exactly the field's present
vectors (ADR039), neighbour counts and ordinals in range without self-edges or
duplicates,
upper levels nested in the level below, the entry point on the top level, every
level-zero node reachable from the entry point, inverse norms that are `0`, `-1`
or inside the conditioned range, empty unused slots and no trailing bytes.

**Construction.** Single-threaded and deterministic: seeded insertion order and
level assignment `floor(-ln(u) / ln(M))`, an efConstruction beam per level, the
diversity heuristic for selection and reciprocal links pruned through it. A node
left with fewer than `M / 2` neighbours is backfilled from rejected candidates in
distance order, and a repair pass links any level-zero node unreachable from the
entry point. Repair keeps a spanning tree of the reached nodes and never removes
a tree edge: it links a node from its nearest reached node with a free slot, or
replaces that node's farthest non-tree link. Such a link always exists, because
full reached nodes hold at least `2 * reached` links against `reached - 1` tree
edges. Full reachability is then verified; a graph that fails it is not
published, and the segment field is searched exactly. All orderings use
`(distance, ordinal)`. Without an
explicit `HnswSeed`, a fixed default seed keeps builds reproducible. Flush builds
over the in-memory field vectors; merge rebuilds from scratch over the merged
`.vec` file. The graph is written before segment publication like any other
segment file.

**Query routing.** A zero query vector has no direction, so cosine is undefined:
it returns no hits on every field, whatever the stored form, searches no segment
and records no route. Otherwise each segment holding the field records exactly
one route, chosen in order:

1. a non-positive query boost or per-document field boosts: exact
   (`ExactScoreTransform`), because the final score is not monotonic in cosine;
2. no graph: exact (`ExactNoGraph`); an unusable graph: exact
   (`ExactGraphUnavailable`);
3. a filter leaving fewer than `max(64, 0.5% of MaxDoc)` documents, or no more
   than the shortlist: exact (`ExactSelectiveFilter`), including a filter that
   matches nothing;
4. otherwise one greedy descent and a level-zero beam of width
   `VectorQuery.GetEffectiveEfSearch`. Filters and deletions restrict results, not
   routing. The budget is `max(4 * ef, eligible upper bound / 3)` distance
   evaluations plus `64 * (maxLevel + 1)` for the descent, and it is a hard limit:
   every evaluation, the entry point and the upper-level descent included, counts
   against it. A graph evaluation was measured at about 1.2 to 1.4 times the cost
   of one vector of the optimised exact scan, so a traversal that completes costs
   at most about 0.45 of the scan it replaces (the 2x speed-up the qualification
   contract requires), and one that exhausts the budget and then scans costs at
   most about 1.45 times exact search alone. Before starting, a traversal whose
   optimistic estimate, `8 * ef / selectivity` capped at the node count, exceeds
   the budget is skipped (`ExactBudgetPredicted`), without wasted work; measured
   traversals evaluated 9 to 34 times `ef / selectivity`. An exhausted budget
   (`ExactBudgetExceeded`) or fewer candidates than the shortlist
   (`ExactUnderfilled`) runs the exact route instead, and the discarded traversal's
   time and evaluations are reported through
   `IMetricsCollector.RecordVectorGraphWastedTraversal`. Routing stays a pure
   function of the query and segment: an adaptive policy that learned from earlier
   queries would make one query's candidates depend on the query history, so it is
   left to qualification to show whether one is needed.

The ADR049 global TopK selection is unchanged: each segment contributes its
rescored shortlist to the one prepared candidate set. Routes are reported through
`IMetricsCollector.RecordVectorSearchRoute` and `Explain`. A fallback is always
recorded as exact and never as approximate search.

**Corruption.** A declared but missing `.vgr` is a structural error, as for
`.hnsw`: the segment cannot open, and `IndexValidator` reports `LLIDX053` once.
Opening a graph validates its frame checksum before any structural check, so a
change that leaves the structure valid is still rejected. A graph that fails
its checksum or validation is never used and the failure is remembered for the
reader state: queries fall back to exact search, `IndexValidator` reports
`LLIDX054`, and recovery treats the role as a recoverable query accelerator,
like BKD trees. A transient open failure (a sharing violation or handle
exhaustion) falls back for that query only and is retried.

**Resources.** A segment whose level-zero adjacency would exceed the maximum
array length is written without a graph and searched exactly. Merge-time builds
observe the writer's merge cancellation, between insertions and between repaired
nodes, and are recorded in build metrics. Every build that the option requested
but did not write, too large or unreachable, is reported through
`IMetricsCollector.RecordVectorGraphBuildSkipped`. The reachability check costs
one level-zero traversal at open: about 15 ms of a 28 ms open for 100,000 nodes
and 185 ms of a 380 ms open for 1,000,000 nodes (M0 = 32, Linux x64, page cache
warm; a cold-storage measurement is part of qualification).
Traversal scratch for graphs of up to about one million nodes is cached per
thread; larger graphs lend scratch from a bounded pool they own, released with
the graph.

**Qualification contract.** Reference parity supersedes ADR042 criterion 1 for
the Uniform matrix only. On every synthetic and real cell, LeanCorpus tie-aware
recall@10 must be no more than 0.02 below the reference implementation (Apache
Lucene 10.5.2, or a recorded hnswlib version) at equal or lower measured
distance evaluations, with the same M and efConstruction. The frozen Uniform and
adverse matrices are still run unchanged and published beside the new results
and the reference results. Their failures are attributed and do not block release
when parity holds. NearDuplicate is judged on tie-aware recall, with literal-ID
recall published beside it. Real-data gates stay as planned: on each of two real
embedding corpora, graph-route queries only, mean recall@1 at least 0.90,
recall@10 and recall@100 at least 0.95, fifth-percentile recall@10 at least 0.80,
across three build seeds, and p95 latency at least 2x lower than the optimised
exact route. Every unfiltered qualification cell must have a zero fallback rate.
This reverses ADR042's statement that no Java feasibility run is a 4.0
dependency: the Lucene reference harness is part of qualification. Platform
scope remains ADR042 criterion 6.

## Rationale

A new role keeps every historical `.hnsw` reader, migration and fixture untouched
and avoids an older-version write lock on indexes that hold historical graphs.
Exact rescoring makes the two routes return identical scores, so routing only
changes which candidates are considered. Fallbacks with explicit, recorded
reasons keep exact search as the correctness floor: a selective filter, an
exhausted budget or an unusable file never produces plausible wrong results.
Reference parity measures the implementation rather than the workload, while the
unchanged historical gates and the real-data gates keep the published bar.

## Consequences

- New persisted additions: the `.vgr` role and `VectorFieldInfo.HasGraph`, absent
  from every existing index. An index containing `.vgr` files is a 4.0 index;
  3.x binaries do not know the role.
- Public API additions: `VectorFieldInfo.HasGraph`, `VectorSearchRoute`,
  `IMetricsCollector.RecordVectorSearchRoute`,
  `IMetricsCollector.RecordVectorGraphWastedTraversal`,
  `IMetricsCollector.RecordVectorGraphBuildSkipped`, `VectorGraphBuildSkipReason`,
  `MetricsSnapshot.VectorGraphRouteCount`, `MetricsSnapshot.VectorGraphFallbackCount`,
  `MetricsSnapshot.VectorGraphWastedTraversalCount`,
  `MetricsSnapshot.VectorGraphWastedEvaluations`, `MetricsSnapshot.VectorGraphWastedTotalMs`,
  `MetricsSnapshot.VectorGraphBuildSkippedCount`,
  `IndexInspectionOverview.HasVectorGraph`,
  `IndexInspectionField.HasVectorGraph`, `IndexInspectionField.VectorGraphNodeCount`,
  and issue codes `LLIDX053` and `LLIDX054`.
- Enabling the option costs build time at flush and on every merge, and roughly
  `(1 + M0) * 4` bytes per vector at level zero. Parallel build and seeding merges
  from an input graph are later optimisations.
- Uniform high-dimensional data gains little from any graph; the documentation
  states this operating envelope.
- Until the qualification contract above has been executed and published, the
  option is implemented and tested but not release-qualified. Acceptance
  restores the writer under ADR042 criterion 7; it does not claim that the
  qualification gates have passed.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-22 | Proposed | Original proposal recorded with the implementation. | `4.0/vector-rebound` |
| 2026-11-22 | Accepted | Owner accepted; checksum validation at open, transient-failure retry, oversized-segment, cancellation and scratch-pooling rules recorded. | `4.0/vector-rebound` |
| 2026-11-23 | Clarified | PR #132 review: conditioned inverse norms with a scaled-cosine marker, spanning-tree repair with a verified reachability postcondition enforced by writer and reader, zero-query and empty-filter route rules, and a measured `eligible / 2` traversal budget. | `4.0/vector-rebound` |
| 2026-11-23 | Clarified | PR #132 second review: a hard budget covering the descent, an `eligible / 3` budget with measured fallback cost, `ExactBudgetPredicted`, wasted-traversal and skipped-build metrics, duplicate-neighbour rejection, cancellation during repair, and measured open cost. | `4.0/vector-rebound` |
