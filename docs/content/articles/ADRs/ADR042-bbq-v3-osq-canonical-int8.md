---
adr: ADR042
title: "BBQ v3: OSQ binary retrieval with stable canonical Int8 vectors"
date: 2026-10-31
status: Proposed
version-added: 4.0.0
summary: "Replace sign-Hamming BBQ with correction-aware OSQ traversal and stable Int8-based reranking; preserve legacy reads and reject lossy automatic upgrades."
areas: [vectors, codecs, hnsw, indexing, search, migration]
supersedes: [ADR016, ADR017, ADR018, ADR019, ADR020]
relates-to: [ADR025, ADR026, ADR039]
---

# ADR042: BBQ v3: OSQ binary retrieval with stable canonical Int8 vectors

- **Date:** 31 October 2026
- **Status:** Proposed until all acceptance evidence is reviewed; after qualification mark Accepted in **this newly authored ADR only**.
- **Supersedes:** ADR016 (experimental hybrid retrieval ship gates as the active design basis for this codec), ADR017 (Matryoshka experimental disposition), ADR018 (RaBitQ experimental disposition), ADR019 (Product Quantisation experimental disposition), ADR020 (hybrid-retrieval research stop). Historical decisions remain immutable evidence and are not edited or retroactively declared incorrect.
- **Continues to respect:** ADR025 unified codec catalogue, ADR026 canonical frame, ADR039 sparse vector presence. Those are infrastructure contracts, not superseded design failures.

## Scope and authority

New BBQ v3 requires `NormaliseVectors=true` and vector dimensions >=128. Other quantisers remain unchanged. The agent must follow the external handoff document `spec/LOCKED-DECISIONS.md` without adding another candidate algorithm or new public storage configuration. This ADR is a **new** immutable-history decision and supersedes the active forward design of ADR016–ADR020 without altering any previous ADR contents.

## Context and problem

LeanCorpus's existing BBQ v2 stores one sign bit per dimension, a per-segment centroid and three aggregate correction statistics. HNSW construction/traversal use a Hamming-style metric; final ranking reconstructs `centroid ± 1`. Under pinned 2026-10-30 attribution, BVAL 64D/128D and integration results fail their release recall requirements even when the source oracle neighbours are in the shortlist. A held-out 10K clustered case visited 10,000 nodes to fill 100 candidates after a graph underfill; that is not acceptable routine HNSW performance. Repeated v2 merges decode and requantise already lost magnitudes, so they accumulate quality degradation. These findings are recorded in the external '4.0 BBQ Engine Correctness' package, with code branch `fix/bbq-retrieval-quality`, commit `3dab5b6`.

The 31 October branch also contains independently useful bounded tie traversal, live deletion shortlist, integer overflow protection, little-endian packing and extreme finite norm improvements. Those fixes remain part of 4.0 and are not evidence that v2 BBQ is qualified.

## Decision

For **new normalised cosine LeanCorpus 4.0 BBQ indexes of dimension >=128**, implement a **new `.vq` body generation v3** inspired directly by Apache Lucene 10.5.2 Optimized Scalar Quantization (OSQ): 1-bit OSQ document codes, prepared 4-bit query codes, optimised scalar intervals, correction metadata and a mathematically corrected approximate cosine/dot estimator. Quantise a query once per field/segment. Stop treating Hamming sign matches as the final BBQ metric.

Persist a second, **stable per-vector unsigned Int8 canonical representation** derived once from original normalised Float32 embeddings, with per-vector offset, scale, sum of codes and decoded squared norm. **Derive OSQ from decoded canonical Int8 and normalise only that temporary view on every flush/merge, including initial indexing; never derive first-generation OSQ from originals directly.** Use this representation for approximate-cosine final reranking and all future merge inputs. Return user-visible scores as **approximate**, not exact original Float32 cosine. Keep the public `VectorQuantisation.BBQ` enum and existing search API; new public precision options are not needed.

Persist OSQ and canonical sections with checked fixed-stride addressing, presence bitmap, version and corruption validation through existing CodecKit frames. On initial flush, prepare canonical codes from originals once, then derive OSQ from the decoded canonical values. At merge, copy canonical bytes and metadata byte-for-byte for surviving logical documents; derive fresh OSQ codes from their decoded, temporarily normalised canonical values. Rebuild v3 graphs after merge because centroid and scorer relationships can change. Keep bounded mapped/file-backed merge passes and existing atomic publish/recovery behaviour.

New v3 graph construction, pruning and traversal use the same prepared, correction-aware scorer with **source/document acting as 4-bit query against 1-bit candidate document** direction, not a mixture of sign-Hamming and cosine. Search uses an automatic v3-only oversampling factor of 3 when source callers omit the parameter (`-1` sentinel); explicit user factors win. Already-compiled callers passing the previous default literal 1 continue to use 1× and this compatibility limit must be documented. Rerank by canonical Int8 estimates. Instrument graph visits and do not normalise full-scan HNSW fallback as an ordinary successful query.

## Compatibility and migration

Historical `.vq` v1/v2 BBQ decoders and graph search remain available for old indexes. Their sign bits and aggregate metadata are **insufficient to reconstruct original per-component magnitudes or the new canonical Int8 representation**. An ordinary CodecKit migration is not a quality upgrade; do not convert those old bodies into v3 OSQ/Int8 silently. Rebuilding v3 requires the original embeddings. Legacy BBQ indexes are query-readable; the 4.0 writer should reject mutation/automatic background merging early unless a separately safe, explicit legacy strategy has been verified. Do not repeatedly fail background merges or publish mixed incompatible graph generations.

The existing 3.1.1 release fixtures (loose/compound/sparse HNSW) and their hash pins stay immutable. Preserve the v1→v2 sparse-presence migration defined in ADR039, including graph-based presence reconstruction and exact preservation of old encoded bytes. CodecKit catalogue registration must not classify `.vq` v2 as a generic v1 presence rewrite after v3 is introduced. The **new v3 body** uses a `storageKind` discriminator: `LegacyInt8`, `LegacyBbqSign`, or `BbqOsqCanonicalInt8`. CodecKit structurally wraps v1/v2 quantised bodies into v3 tagged legacy storage after authentic presence reconstruction for v1, copying original quantised bytes without requantisation; these legacy tags keep the old scorer and do not become the new BBQ algorithm. No non-current v2 writer is required. Extend tests rather than loosening migration validation.

**Eligibility:** new BBQ v3 is rejected before any write if dimension <128 or the normalisation setting is disabled. No silent downgrade to standalone Int8. Smaller 32/64D historical BBQ indexes remain readable and structurally migratable. Existing pinned 32/64D failures remain historical diagnostics; new 4.0 qualification operates at supported 128/384/768D. The >=65% measured vector-payload reduction gate is unchanged: at 128D the exact logical proposed payload is 176 bytes versus 512 bytes Float32 (65.625% reduction before per-segment overhead); 32D and 64D cannot meet it under this format.

Normal standalone Int8 and Float32 semantics remain as before. Imports that convert existing stand-alone Int8 into new canonical Int8 can introduce a single lossy conversion; record this fact and do not claim Float32-fidelity. There is **no** implicit v2 BBQ→v3 path.

## Alternatives rejected for 4.0

- **Keep v2 and increase `efSearch`/oversampling:** held-out attribution shows final reconstructed scoring itself loses correct neighbours, even with complete candidate sets.
- **Use 8-bit query with binary docs but no rerank:** cannot recover source document magnitude; adds complexity without sufficient evidence.
- **Retain Float32 in every v3 index:** enables exact rescoring but sacrifices disk-saving goals for this release. This is Lucene/Elasticsearch's more accurate trade-off, not an error in those systems.
- **Allow Int8 or Float16 user-configurable canonical stores:** doubles persisted contracts and migration/merge/test combinations. Float16 is deferred.
- **Revisit RaBitQ, PQ, 2-bit/4-bit groups now:** expands scope into further experiments and risks failing the 4.0 readiness window. This ADR chooses one reference algorithm and asks for quality-gate proof.
- **Copy an entire other .NET engine:** Qvec and Hnsw.Net offer useful storage/parity patterns, not drop-in LeanCorpus codecs.

## Rationale and expected costs

At 768 dimensions, v3's raw vector payload is approximately `96 bytes` OSQ bits + `768 bytes` canonical codes + `16 bytes` OSQ correction + `16 bytes` canonical metadata = `896 bytes` per vector, excluding centroid, graph, presence, frames, alignment and other index data. Float32 alone is `3072 bytes` per vector: around 3.43× smaller vector payload, **not** a 32× total-index claim. The 1-bit working set remains compact for HNSW traversal. Canonical Int8 remains lossy, especially for tight cosine clusters; separate release qualification is mandatory. Some low-dimensional distributions may remain difficult.

This architecture reuses LeanCorpus's random-access and file-backed merge machinery, Lucene's tested quantiser and score reconstruction, and Qvec's stable Int8 corrected arithmetic. The mathematical and storage contracts are explicit, AOT-compatible and independent of native dependencies.

## Release qualification and operational consequences

- **Supported new BBQ v3** dimensions 128/384/768D must meet the existing BVAL recall floor (>=0.90 at the specified K) and new integration floor >=0.70, across multi-query held-out distributions. The original 32/64D cases remain immutable legacy reproduction/diagnostic and read/migration coverage, not v3 promotion cases. No recall threshold is reduced for supported v3. Measure candidate coverage versus final rank separately.
- Confirm at least 65% lower **persisted** vector-payload bytes (both codes, both metadata blocks, presence and amortised centroid) than matched Float32 **for each qualifying dimension** and p95 latency no worse than 1.10 times matched Int8 (historic research comparison gates, not a substitute for the additional 4.0 reliability gates). Complete 10K and 100K tests with predeclared `efSearch`, default oversampling, HNSW config and source fingerprints. Include Float32/Int8 controls, merge generations, disk bytes, p50/p95 latency, allocations and graph visits.
- Test deterministic graph builds, exact byte-preserving canonical merges under deletion/sorting/sparse/compound, crash recovery, migration, Unicode/metadata/checksum corruption, Windows mapped handle lifetime, Linux/Windows, .NET 10/11 and Native AOT.
- Retain old `.vq` v1/v2 reader semantics, expose clear error when attempting to mutate an old BBQ index into v3 without reindexing originals; document a safe operator rebuild procedure.
- Do not mark this ADR Accepted, or ship BBQ v3, if mandatory quality, compatibility, persistence or CI gates are unqualified.

## Beyond 4.0: prior research revisited deliberately

Earlier ADRs retain their historical outcomes; this decision **supersedes their role as the active forward vector design** but not the factual findings. In 5.0 or later, revisit in order of evidence:

1. **Product Quantisation (PQ):** the second two-level 256-centroid implementation reached recall@10 0.938/0.794 at default 64D/128D and 0.988/0.994 with `efSearch=512`, with about 68.2% payload reduction. Its principal unresolved issue was 128D default-budget HNSW routing, not a lack of codec potential. Start from preserved `vnext-hybrid-failed` implementation and confirm an independently qualified routing/latency envelope.
2. **RaBitQ:** the earlier rotated-sign experimental implementation missed quality and allocation gates (64D 0.194 and 128D 0.156 recall@10). Reconsider only a complete reference-verified asymmetric native-bit-distance implementation, not the previous per-candidate Float32 reconstruction.
3. **Float16 and other canonical layouts:** reconsider only if stable Int8 precision is materially insufficient for an actual supported workload, using per-codec versioned compatibility and measured storage/latency.
4. **Higher-bit / residual / rotated or adaptive quantisers, Matryoshka, learned sparse, late interaction and hybrid retrieval:** independent, separately qualified proposals; no in-place retrofits to 4.0 v3 and no compatibility promises before data and gates.

No current 4.0 acceptance gate can be waived merely because a later roadmap codec might improve it.

## Reference implementation and tests

- Apache Lucene 10.5.2 (Apache-2.0): `OptimizedScalarQuantizer.java`, `QuantizedByteVectorValues.java`, `Lucene104ScalarQuantizedVectorScorer.java`, `Lucene104ScalarQuantizedVectorsWriter.java`, `Lucene104ScalarQuantizedVectorsReader.java`, `TestOptimizedScalarQuantizer.java`.
- Qvec (Apache-2.0): `docs/design-quantization-int8.md`, `docs/design-quantization-rescoring.md`, `Qvec.Core/QvecDatabase.cs`.
- Hnsw.Net (MIT): `src/Hnsw.Net/HnswIndex.cs`, in particular `SelectNeighbors` and related parity tests.
- Branch and test evidence as specified in the external package `LeanCorpus-BBQ-v3-Implementation`.

## Changes

| Date | Type | Change |
|---|---|---|
| 2026-10-31 | Proposed | Superseding BBQ v3 architecture and migration decision; acceptance pending implementation and qualification. |
