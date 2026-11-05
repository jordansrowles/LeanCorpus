---
adr: ADR042
title: "Vector codec retirement and exact Float32 support"
date: 2026-10-31
last-updated: 2026-11-05
status: Accepted
version-added: 4.0.0
summary: "Support exact Float32 writes and search; retire new HNSW and Int8 writes while preserving compatible historical reads."
areas: [vectors, codecs, hnsw, indexing, search, migration]
supersedes: [ADR016, ADR017, ADR018, ADR019, ADR020]
relates-to: [ADR025, ADR026, ADR039]
---

# ADR042: Vector codec retirement and exact Float32 support

- **Date:** 31 October 2026, revised 5 November 2026.
- **Status:** Accepted. This records the product and compatibility decision; it does not claim release qualification.
- **Supersedes:** the active forward vector design of ADR016 through ADR020. Their published findings remain immutable historical evidence.
- **Preserves:** ADR025's single catalogue, ADR026's framing contract and ADR039's explicit sparse presence.

## Context

Historical sign-BBQ loses per-component magnitudes and cannot reconstruct original
embeddings. The one-bit OSQ experiment added canonical Int8 reranking but did not
qualify. On 31 October, the pinned 300-document, 128D integration case returned
recall@10 of 0.60 against its 0.70 floor. The graph returned the exhaustive OSQ
top-30, containing only six original Float32 top-10 neighbours. Canonical Int8
reranking also returned 0.60. Independent Lucene reference execution reproduced
the relevant candidate sets. This was a scoring/shortlist quality failure, not
proof of a graph traversal defect.

A later single-query two-bit reference result of 0.80 is historical research
motivation. It does not qualify a new writer or a supported release. Neither
result permits weakening the frozen quality gates.

## Decision

Float32 exact search is supported for new vectors. Frozen qualification passed
the exact Float32 gates but failed the HNSW traversal gate. Int8 exact search
passed only part of the matrix; the frozen merge drift gate failed. Therefore
LeanCorpus 4.0 retires new Int8 writes and new HNSW graph writes. New vector
indexes contain Float32 values only and use exact scoring. This keeps a
qualified, deterministic retrieval floor without weakening the frozen gates.

`BuildHnswOnFlush` defaults to false. Explicit true is rejected before index
side effects. Historical Float32 and Int8 `.hnsw` graphs are retained as data
but ignored by those query routes; searches use exact scans and report that
choice through metrics and explanations. Float32 merges preserve vectors but
do not create graphs. Merging a historical Int8 segment decodes its stored
values and writes them to Float32 `.vec` v2, preserving the decoded exact
scores without emitting new Int8 data. Retained HNSW helpers remain available
for compatibility, structural migration, and historical fixtures, not as a
supported index writer path.

Disable every BBQ creation and mutation path, including configuration of an
empty index, flush, update, import, merge and migration. Never silently
substitute another quantiser.

Any committed BBQ field makes the entire containing index read-only. This also
applies to mixed indexes, all-deleted BBQ documents and text-only changes.
Writer opening must refuse before recovery mutations or background work.
Imports and migrations preflight the complete source before staging and
revalidate before execution or publication. Temporary exclusion locks may be
acquired and released. Refused operations preserve committed source data.

Historical Int8 remains readable and is searched exactly over its decoded
stored values. This is not exact recovery of source Float32 ranking. Creation
of new Int8 encodings is retired, while historical Int8 v1/v2-to-v3/tag-1
structural migration remains supported without requantisation. A normal merge
may also decode historical Int8 and write Float32 `.vec` v2; it does not emit
new Int8 data. Historical BBQ remains readable and searchable with its
existing scorer and explicit quality limitations, but any containing index is
read-only to mutation and migration. Structural readability is not recall
qualification. Preserve numeric tags and the known experimental one-bit
`BbqOsqCanonicalInt8` interpretation; never reassign that tag to two-bit data.
Retain required query preparation and immutable historical fixtures. Operators
requiring new vector indexing from BBQ data must rebuild a separate Float32
index from their original documents and embeddings. Decoding BBQ is not
recovery of the originals.

## Catalogue lifecycle

Extend the existing CodecKit descriptors with representation-aware decisions for
`Read`, `Create`, `RewriteSource` and `Migrate`, plus explicit migration routes.
Version-level `IsReadable`, `IsWritable` and `MigrationBehaviour` remain ceilings.
An optional immutable representation policy can restrict them, never grant
otherwise prohibited access. Unknown identities fail closed.

A readable retired v7 can coexist with a current v8 writer without a v7-to-v8
migration route. A completely retired file role retains its latest known
`CurrentFormatVersion` and resolves no `WritableFormatVersion`. It is distinct
from a versionless role. Preserve existing public constructor signatures and
unrelated catalogue registrations. Do not add another codec or version registry.
Descriptor frame writers check lifecycle before emitting bytes; semantic writers
also supply the validated representation identity. Raw framing remains a byte
primitive, not a substitute for semantic lifecycle enforcement.

## Formats and migration

Keep Float32 `.vec` v2, tagged Int8 `.vq` v3 and `.hnsw` v1 while their physical
contracts remain unchanged. Product version 4.0 does not rename physical body
versions. No current semantic writer emits `.vq` or `.hnsw`; historical
readers and approved structural migration remain. A demonstrated incompatible
encoding requires a separate reviewed identity/version decision.

Preserve verified Int8 v1/v2-to-v3 structural migration. Copy encoded values,
minimum/scale, corrections, document order and presence without requantisation.
v1 presence requires authoritative persisted graph membership; absent proof is
an unsupported migration. No original precision is recovered. New Int8 writes
remain retired. Ordinary merges of historical Int8 materialise its decoded
values as Float32 and do not apply the unqualified zero-centred calibration.

All BBQ migrations are unsupported, including tagged current-version no-op
requests. Report read-only retirement before returning AlreadyCurrent. Mixed
indexes are rejected as a whole. Old staged plans cannot resume into new BBQ
publication. Restoring an already committed, checksum-valid historical generation
byte-for-byte is a separate recovery operation and remains permitted; it cannot
finish an uncommitted conversion or regenerate vectors/graphs.

## Qualification and release status

The final 4.0 decision is exact Float32 for new vector writes and search. The
measurements below explain why HNSW, new Int8 and BBQ creation remain retired;
they preserve the original failed gates and do not authorise relaxed thresholds.

**BBQ retrieval.** The pinned 300-document, 128D integration case returned
recall@10 `0.60` against the frozen `0.70` floor. The graph returned the
exhaustive OSQ top-30, which contained only six of the original Float32 top-10
neighbours. That candidate set caps source-relative recall@10 at `0.60` for
the case. Canonical Int8 reranking also returned `0.60`, and an independent
Lucene reference reproduced the candidate sets. Sign-BBQ discards per-component
magnitudes, so its decoded representation cannot reconstruct arbitrary
query-weighted residual scores. Candidate coverage and representation scoring
both failed; the measurement does not establish a graph traversal defect. A
later single-query two-bit result of `0.80` remains research-only.

**Float32 HNSW.** On the frozen 10K Uniform matrix, the unchanged default
EF64 passed `0/8` dimension/query-seed groups. Tuned EF400 passed `6/8` and
still failed both 768D groups. On the separate 300-by-64 adverse matrix,
`12/14` groups passed at each EF64, EF128, EF256 and EF400; both NearDuplicate
groups failed at every budget. A selected 384D EF400 example visited about
6,900 of 10,000 graph nodes. No matched latency run exists, so larger EF does
not qualify either recall or a useful performance advantage. The selected
captures lack some per-step frontier traces and do not prove global graph
optimality.

**Int8.** No persisted candidate passed all 20 adverse groups; tested schemes
passed `12/20` to `16/20`. Float32 normalisation alone passed `19/20`, with its
single source-literal miss kept separate from persisted-Float32 oracle
correctness. The 10K Uniform v3/tag-1 control passed `7/8` groups and failed
384D/query-seed 42 at recall@1 `0.750`. Decoded-dot graph traversal and
decoded-cosine reranking also used inconsistent score functions; that
diagnostic was not promoted into a new scorer or encoding. The four-generation
Int8 merge trajectory was `1.000, 1.000, 0.975, 0.900` mean recall@10. The
successive drops of `0.025` and `0.075` exceed the frozen `0.020` limit. The
generation-four decline separates into `0.050` corpus-growth loss and `0.025`
same-corpus merge loss.

**Exact Float32 and performance.** The persisted Float32 exact path passed its
independent score and ranking contract: score error at most `1e-5`, every
neighbour more than `1e-6` above the Kth score included, and remaining results
within the boundary tie group. This is correctness evidence, not a speed claim.
No HNSW or Int8 candidate passed the required quality gates, so no 100K
qualification or matched vector performance campaign ran. No latency,
throughput, storage savings or universal ANN/exact speed ratio is claimed.

### Compatibility and persisted formats

The final implementation keeps `.vec` body v2, `.vq` body v3 and `.hnsw` body
v1. It adds no storage tag, body version, package version or new migration
route. Historical Int8 tag 1 remains readable; v1/v2 Int8 can be structurally
migrated to v3/tag 1 while preserving encoded values, calibration, corrections
and presence without requantisation. Version-1 presence reconstruction
requires validated persisted HNSW membership. Merging historical Int8 writes
the decoded values as Float32 `.vec` v2. Historical HNSW bytes remain
readable; Float32 and Int8 exact searches ignore them. Historical BBQ keeps its
established read/search semantics and blocks mutation of its whole index.
BBQ-tagged migration, including a current-version no-op, remains denied.

### Final CI and evidence limits

On the implementation baseline `db096a01695aa2a34b4452811198eddc8ecd73a2`,
focused local vector tests passed `32/32` on .NET 10.0. Migration and backup
regressions passed `3/3` on both .NET 10.0 and .NET 11.0. Hosted workflow
`41937821632`, attempt 3, passed all `19/19` jobs; its Windows Core retry passed
`20/20` targets. Attempts 1 and 2 recorded Windows cleanup failures, which
were resolved by retries on the same source revision. The hosted checks cover
implementation and compatibility gates; they do not turn the failed ANN,
quantisation or BBQ retrieval measurements into passes. The closeout follow-up
updates guidance and diagnostics without changing vector ranking or persisted
formats; its exact-head validation is recorded in the accompanying task
handoff.

The frozen 2,382-row ledger remains unchanged: 1,806 rows are `Unqualified`
and 576 are `NotRunPerDependencyFailure`; none are promoted. The historical
diagnostic captures used earlier source revisions and dirty diagnostic
worktrees. Their provenance remains in the external evidence pack, and missing
frontier traces limit HNSW causal claims. Skipped 100K work and absent matched
performance results remain open evidence gaps, not inferred passes. There is no
release or merge-readiness claim in this ADR beyond the supported exact
Float32 contract and the stated compatibility behavior.

## Alternatives

Scattered BBQ conditionals are insufficient because import, migration and
background paths can bypass them. A second lifecycle registry would duplicate
existing catalogue capabilities. The selected approach extends the existing
catalogue and applies shared index-level preflight.

Continuing the one-bit writer, raising budgets silently, weakening recall floors
or interpreting old BBQ as recovered Float32 would preserve an unqualified
release path. These are excluded from 4.0.

## Deferred research

The following work has no schedule or release commitment and is not a
guaranteed LeanCorpus 5.0 deliverable:

- Diagnose HNSW graph quality and navigability, including construction,
  topology, entry-point behaviour and high-dimensional/adverse data failures.
  Any traversal candidate must pass the frozen recall matrix without counting
  exhaustive underfill fallback as ANN success.
- Test cosine-consistent Int8 retrieval so graph traversal, exact decoded
  scoring and reranking use a compatible metric. Keep source-relative quality
  separate from decoded-representation exactness.
- Investigate per-vector Int8 encoding and merge stability across generations.
  Keep the absolute group floors and the maximum successive mean recall@10
  loss of `0.020`; report corpus growth separately from same-corpus merge loss.
- Research two-bit BBQ first. Consider four-bit only if two-bit fails its
  complete quality, compatibility and cost gates. Either candidate may remain
  unshipped. Preserve tags 1 through 3 and their existing interpretations.
- Profile and improve exact Float32 search while preserving the independent
  persisted-value score and tie contract. Report any speed or storage claim
  only from matched, controlled measurements.

No Java feasibility run, OSQ tuning, PQ or RaBitQ implementation is a 4.0
dependency. Published identities cannot be silently reinterpreted by later
research. Upstream reference names and licences remain exact, including Apache
Lucene's `OptimizedScalarQuantizer`. Retained LeanCorpus-owned quantiser code
uses `OptimisedScalarQuantiser.Quantise`; this naming change must preserve
historical reader/query semantics.

## Reintroduction criteria

No retired approximate or quantised write path may return based on a single
query, a tuned diagnostic, a focused pass or CI success alone. Before review of
any new candidate, retain all original fixtures, source truth, quality floors,
negative controls and per-group reporting. At minimum:

1. Pass BVAL at K=1, 10 and 100 with recall of at least `0.90` against original
   Float32 source truth, including the correct source neighbour at K=1. Pass
   each held-out four-query group with mean recall@10 at least `0.90`, every
   query at least `0.80`, and mean recall@1 and recall@100 at least `0.90`.
   Do not pool groups to hide a failure. The BBQ integration floor remains
   `0.70` recall@10.
2. For Int8, pass both the source-relative gates and the independent exact
   decoded-Int8 cosine contract. For each merge generation, retain the absolute
   group floors and keep every successive mean recall@10 loss at or below
   `0.020`; report same-corpus merge and corpus-growth effects separately.
3. For HNSW, pass the fixed graph and source quality requirements with
   deterministic topology and transparent visit/fallback accounting. An
   exhaustive fallback cannot count as an ANN pass.
4. Pass the full relevant loose/compound, sparse, delete, filter, recovery,
   migration, merge, package-consumer and cross-platform compatibility tests.
   Define any new persisted representation with a reviewed identity and
   migration policy; never reinterpret an existing `.vq` tag or silently
   change a body version.
5. After quality passes, show a material benefit against matched exact
   Float32 search using warm p50/p95 latency, allocations, throughput, build
   and merge cost, graph visits, vector/graph bytes and whole-index size on
   intended 10K and 100K workloads. No universal speed ratio is assumed.
6. Run the prescribed .NET 10/11 Linux and Windows validation, applicable AOT,
   package/consumer and documentation workflows on the exact proposed source
   revision. Publish failed, skipped and incomplete evidence with its limits.
7. Record a new owner decision and accepted ADR before restoring any writer,
   benchmark or public default. Passing these gates permits review; it does
   not promise a particular release or schedule.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-10-31 | Proposed | Initial one-bit OSQ/canonical Int8 experiment; qualification failed. | `6bba01a` |
| 2026-11-02 | Proposed | Retire all BBQ mutation and qualify supported Float32/Int8; defer new BBQ research. | Vector reliability and codec retirement pack |
| 2026-11-03 | Proposed | Record zero-centred Int8 calibration under the existing v3 body and the owner decision to keep v3. | Vector reliability execution evidence |
| 2026-11-04 | Accepted | Retire new HNSW and Int8 writes after frozen gates failed; retain exact Float32 and historical compatibility routes. | `LeanCorpus_4_Vector_Closeout_Luna_Implementation_Pack` |
| 2026-11-05 | Accepted | Record measured qualification outcomes, clarify historical Int8 structural migration and merge-to-Float32, and define deferred research and reintroduction criteria. | 4.0 vector closeout follow-up |
