---
adr: ADR042
title: "Vector codec retirement and Float32/Int8 qualification"
date: 2026-10-31
last-updated: 2026-11-02
status: Proposed
version-added: 4.0.0
summary: "Retire all BBQ mutation, preserve historical reads, and require independent Float32 and Int8 qualification."
areas: [vectors, codecs, hnsw, indexing, search, migration]
supersedes: [ADR016, ADR017, ADR018, ADR019, ADR020]
relates-to: [ADR025, ADR026, ADR039]
---

# ADR042: Vector codec retirement and Float32/Int8 qualification

- **Date:** 31 October 2026, revised 2 November 2026.
- **Status:** Proposed. Implementation and mandatory qualification remain incomplete.
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

LeanCorpus 4.0's new vector output is Float32 and Int8 only, conditional on their
own qualification. Disable every BBQ creation and mutation path, including
configuration of an empty index, flush, update, import, merge and migration.
Never silently substitute another quantiser.

Any committed BBQ field makes the entire containing index read-only. This also
applies to mixed indexes, all-deleted BBQ documents and text-only changes.
Writer opening must refuse before recovery mutations or background work.
Imports and migrations preflight the complete source before staging and
revalidate before execution or publication. Temporary exclusion locks may be
acquired and released. Refused operations preserve committed source data.

Historical BBQ remains readable and searchable with its existing scorer and
explicit quality limitations. Structural readability is not recall
qualification. Preserve numeric tags and the known experimental one-bit
`BbqOsqCanonicalInt8` interpretation; never reassign that tag to two-bit data.
Retain required query preparation and immutable historical fixtures. Operators
requiring writable data must create a separate Float32 or Int8 index from their
original embeddings. Decoding BBQ is not recovery of the originals.

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
versions. A demonstrated incompatible encoding requires a separate reviewed
identity/version decision.

Preserve verified Int8 v1/v2-to-v3 structural migration. Copy encoded values,
minimum/scale, corrections, document order and presence without requantisation.
v1 presence requires authoritative persisted graph membership; absent proof is
an unsupported migration. No original precision is recovered. Ordinary Int8
merges can change per-segment scales and must be qualified for distortion
separately from byte-preserving structural migration.

All BBQ migrations are unsupported, including tagged current-version no-op
requests. Report read-only retirement before returning AlreadyCurrent. Mixed
indexes are rejected as a whole. Old staged plans cannot resume into new BBQ
publication. Restoring an already committed, checksum-valid historical generation
byte-for-byte is a separate recovery operation and remains permitted; it cannot
finish an uncommitted conversion or regenerate vectors/graphs.

## Qualification

Prove each layer in dependency order:

1. Exact Float32 against an independent scalar source-vector oracle.
2. Float32 HNSW at frozen budgets, with explicit traversal/scan attribution.
3. Exact Int8 against independently decoded stored semantics, and separately against original Float32.
4. Int8 HNSW against both references to distinguish quantisation and graph errors.
5. Persistence, structural migration, repeated merges, recovery and Windows lifetimes.
6. Complete frozen 10K/100K quality, matched exact/ANN costs and exact-head Linux/Windows .NET 10/11, AOT, package, consumer, managed DevOps, server/Orchard and documentation CI.

Primary ANN requires zero exhaustive underfill fallbacks. Preserve the fallback
for correctness and report cumulative graph work separately from scanned
vectors. Intentional selective-filter scans are a distinct strategy. Use clean
resolved defaults, no result cache, automatic Float32/Int8 oversampling of 1 and
expected default ef=max(64,4K); explicit user settings remain supported.

BVAL recall@1/10/100 must each reach 0.90. Four-query confirmatory groups require
mean recall@1/@10/@100 of at least 0.90 and every recall@10 at least 0.80. Apply
absolute floors at every merge generation, with no successive mean @10 drop
above 0.02. Exact Float32 requires correct ranking under the independent
score/tie contract, rather than an ANN floor. Preserve the immutable BVAL fixture.

Measure complete matched exact/ANN latency, allocations, graph/scan costs,
vector/graph/whole-index bytes, regressions and crossover points. There is no
inherited BBQ storage-saving threshold, BBQ/Int8 latency gate or universal
ANN/exact speed ratio. Unexplained material regressions remain Unqualified.
Missing, failed or incomplete mandatory evidence cannot establish release or
merge readiness. Keep this ADR Proposed until implementation and gates pass.

### Qualification record

The earlier one-bit experiment remains Unqualified. The replacement Float32/Int8
matrix and retirement implementation are in progress; no release qualification
is claimed by this decision. External execution records contain actual run IDs,
source/configuration hashes and unresolved failures.

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
