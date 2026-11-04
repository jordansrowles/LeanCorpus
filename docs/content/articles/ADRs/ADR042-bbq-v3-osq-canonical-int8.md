---
adr: ADR042
title: "Vector codec retirement and exact Float32 support"
date: 2026-10-31
last-updated: 2026-11-04
status: Accepted
version-added: 4.0.0
summary: "Support exact Float32 writes and search; retire new HNSW and Int8 writes while preserving compatible historical reads."
areas: [vectors, codecs, hnsw, indexing, search, migration]
supersedes: [ADR016, ADR017, ADR018, ADR019, ADR020]
relates-to: [ADR025, ADR026, ADR039]
---

# ADR042: Vector codec retirement and exact Float32 support

- **Date:** 31 October 2026, revised 4 November 2026.
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
stored values. This is not exact recovery of source Float32 ranking.
Structural Int8 v1/v2-to-v3/tag-1 migration remains supported without
requantisation, but Int8 merge rewrites are retired. Historical BBQ remains
readable and searchable with its existing scorer and explicit quality
limitations. Structural readability is not recall qualification. Preserve
numeric tags and the known experimental one-bit `BbqOsqCanonicalInt8`
interpretation; never reassign that tag to two-bit data. Retain required query
preparation and immutable historical fixtures. Operators requiring writable
data must create a separate Float32 index from their original documents and
embeddings. Decoding BBQ is not recovery of the originals.

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

The implementation must preserve the selected write and read behaviour across
flush, search, merge, migration, recovery, CLI, benchmarks and documentation.
Release qualification remains a separate evidence decision and requires the
pack's exact-head Linux/Windows .NET 10/11, AOT, package, consumer, managed
DevOps, server/Orchard and documentation gates. Retired ANN/Int8 quality rows
remain failures in the frozen evidence ledger; their retirement does not turn
them into passes.

Float32 exact ranking uses the independent source-vector oracle. Historical
Int8 exact ranking is defined against decoded stored values, not source
Float32. Historical BBQ retains its recorded scorer and quality limitations.
Exact vector scans report scan counts separately from historical BBQ graph
work and intentionally ignored graph counts.

The frozen BVAL and four-query floors remain unchanged for research and for any
future request to restore approximate routes. Apply merge-generation floors
without weakening them. Exact Float32 requires correct ranking under the
independent score/tie contract, rather than an ANN floor. Preserve the
immutable BVAL fixture.

Historical qualification measurements remain evidence, not a product promise.
Any future approximate route requires matched exact/ANN latency, allocations,
graph/scan costs, vector/graph/whole-index bytes, regressions and crossover
points. There is no inherited BBQ storage-saving threshold, BBQ/Int8 latency
gate or universal ANN/exact speed ratio. Missing, failed or incomplete
mandatory release evidence cannot establish release or merge readiness.

### Qualification record

The earlier one-bit experiment remains Unqualified. Exact Float32 passed its
focused net10/net11 oracle and DataForge contract runs. Float32 HNSW failed all
eight frozen EF64 groups; Int8 candidate and four-generation merge gates also
failed. The implementation retires those new write/search routes without
changing their frozen ledger rows. No v4 representation, migration route, or
v3 reinterpretation is approved. No release qualification is claimed.
External execution records contain actual run IDs, source/configuration hashes
and unresolved release gates.

## Alternatives

Scattered BBQ conditionals are insufficient because import, migration and
background paths can bypass them. A second lifecycle registry would duplicate
existing catalogue capabilities. The selected approach extends the existing
catalogue and applies shared index-level preflight.

Continuing the one-bit writer, raising budgets silently, weakening recall floors
or interpreting old BBQ as recovered Float32 would preserve an unqualified
release path. These are excluded from 4.0.

## Deferred research

New BBQ belongs to LeanCorpus 5 research: independently qualify two-bit first,
then consider four-bit if two-bit fails. Either can remain unshipped. No Java
feasibility run, OSQ tuning, PQ or RaBitQ implementation is a 4.0 dependency.
Published identities cannot be silently reinterpreted by later research.

Upstream reference names and licences remain exact, including Apache Lucene's
`OptimizedScalarQuantizer`. Retained LeanCorpus-owned quantiser code uses
`OptimisedScalarQuantiser.Quantise`; this naming change must preserve historical
reader/query semantics.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-10-31 | Proposed | Initial one-bit OSQ/canonical Int8 experiment; qualification failed. | `6bba01a` |
| 2026-11-02 | Proposed | Retire all BBQ mutation and qualify supported Float32/Int8; defer new BBQ research. | Vector reliability and codec retirement pack |
| 2026-11-03 | Proposed | Record zero-centred Int8 calibration under the existing v3 body and the owner decision to keep v3. | Vector reliability execution evidence |
| 2026-11-04 | Accepted | Retire new HNSW and Int8 writes after frozen gates failed; retain exact Float32 and historical compatibility routes. | `LeanCorpus_4_Vector_Closeout_Luna_Implementation_Pack` |
