---
adr: ADR016
title: Experimental hybrid retrieval requires measured ship gates
date: 2026-07-28
status: Accepted
version-added: vNext
summary: Require measured ship gates for experimental hybrid retrieval.
areas: [search, vectors, governance]
---

# ADR016: Experimental hybrid retrieval requires measured ship gates

- **Date:** 2026-07-28
- **Status:** Accepted

## Context

Matryoshka retrieval, RaBitQ, product quantisation, and compressed
late-interaction retrieval add substantial storage-format, scoring,
maintenance, and Native AOT complexity.

Publishing a stable API or codec format before proving its value creates a
long-term compatibility commitment. Completing a prototype does not establish
that it improves LeanCorpus over Float32, Int8, BBQ, four-bit scalar
quantisation, BM25, dense retrieval, or reciprocal rank fusion.

## Decision

Each capability begins as internal or explicitly experimental. Its tracker
entry links the benchmark definition, baseline, results, and final decision.

Correctness, deterministic persistence, migration behaviour, fallback
execution, and Native AOT validation are mandatory before performance results
are considered. Measurements use representative data, an exact reference
implementation, matched candidate or recall budgets, and the strongest
relevant existing LeanCorpus baseline.

A capability becomes stable only when it produces a material, repeatable
improvement in at least one declared primary measure, such as retrieval
quality, recall, p95 latency, working set, or index size, without violating its
declared correctness and operational guard rails.

The workload, primary measure, baseline, acceptable regressions, and promotion
threshold are recorded in the epic tracker before the final benchmark run.
They are not relaxed after results are known.

A failed experiment receives a benchmark report and an explicit `Rejected`
decision. Its stable public API and production codec path are removed.
Independently useful kernels, tests, benchmark fixtures, and research evidence
may remain. Inconclusive results remain experimental and do not complete the
epic. They must receive enough measurement to reach a decision or be rejected.

Late-interaction retrieval must also prove useful retrieval quality on a
labelled workload and bounded full-corpus candidate generation. Exact MaxSim
correctness alone is not sufficient for promotion.

## Rationale

Vector codec and retrieval choices trade quality, latency, memory, index size,
build cost, merge cost, portability, and implementation complexity. No single
technique dominates across embedding families or workloads.

Defining the gate before the final measurement prevents a technically
interesting prototype from becoming a permanent compatibility burden without
evidence. It also allows LeanCorpus to retain reusable implementation work when
a complete product surface is not justified.

## Consequences

- Documentation distinguishes stable, experimental, and rejected capabilities.
- Experimental codec identifiers and formats are not compatibility promises.
- Changelog and feature-parity material cannot describe an experiment as
  shipped before promotion.
- Every promotion or rejection records the tested commit, environment, corpus,
  configuration, commands, result artefacts, and rationale.
- The Hybrid Retrieval 2.0 plan and tracker are the source of the individual
  gate definitions and decisions.

## Recorded decisions

### Matryoshka retrieval: Rejected, 2026-07-30

The 100,000-document deterministic-vector run at
`bench/debian/2026-07-30/11-02` compared prefix rescoring directly with the
same-run Float32 HNSW baseline. Prefix rescoring took 26.2-31.1 ms and allocated
30-55 MB per operation; Float32 HNSW took 2.28-4.11 ms and allocated 40-46 KB.
It missed the declared latency gate of at most 0.75x the full-vector baseline by
7-12x. The public query, prefix metadata, execution path, diagnostics, cache
identity, tests, and normal benchmark path were removed.

### Product quantisation: Rejected, 2026-07-30

The 100,000-document deterministic-vector run at
`bench/debian/2026-07-28/21-05` measured recall@10 of 0.069 at 64 dimensions and
0.031 at 128 dimensions, against the declared minimum of 0.90. It also allocated
5.1 MB and 11.3 MB per operation. That first prototype used only 16 centroids,
reconstructed a Float32 vector for each graph comparison, and built its graph
over a different representation. It was rejected and must not be used as
evidence about PQ itself.

The replacement experiment uses deterministic bounded-sample k-means and 256
centroids. A fast isolation sweep at 100,000 documents showed that
one-dimensional subspaces achieved 1.000 and 0.994 direct recall@10 at 64 and
128 dimensions, while four-dimensional subspaces retained 0.925 and 0.938 of
the exact top ten inside a 40-document candidate window. The production
experiment therefore stores four-dimensional routing codes for code-native
graph construction and HNSW scoring, plus one-dimensional final codes for
shortlist reranking.

The two streams reduce the vector payload by approximately 68.2% against
Float32 at 100,000 documents. Fixed HNSW and segment files are reported
separately because they cannot be reduced by a vector codec, and made the
original whole-index 65% threshold mathematically impossible at 64 dimensions.
The corrected storage gate is at least 65% fewer vector payload bytes than
Float32, with total index bytes still recorded as an operational measure. The
experiment may be run in the normal VQ suite solely to collect a new ADR016
decision. It is not promoted until it meets every remaining PQ gate.

A focused 100,000-document short run at
`bench/debian/2026-07-30/17-13` measured 2.026 ms and 3.299 ms search means at
64 and 128 dimensions. Default recall@10 was 0.919 and 0.781, while
`efSearch=512` reached 0.963 and 0.975. This isolated the remaining loss to
shortlist traversal rather than final-code distortion. The final revision
retained four times `topK` routing candidates for final-code reranking and
pooled lookup buffers.

The one permitted confirmation at `bench/debian/2026-07-30/17-32` measured
0.938 recall@10 at 64 dimensions but only 0.794 at 128 dimensions, below the
declared 0.90 threshold. Exhaustive final-code recall was 0.988 and 0.994, and
`efSearch=512` recall was 0.988 and 0.994, confirming that the remaining
failure was default-budget HNSW traversal. Search means were 2.152 ms and
3.745 ms, with 42.85 KB and 49.36 KB allocated per operation. These remain
below the prior same-host Int8 means of 2.713 ms and 4.476 ms, while the
combined vector payload remained approximately 68.2% smaller than Float32.

PQ therefore passed its vector-payload and latency measures but failed its
mandatory 128-dimensional recall gate. Raising the production search budget
to 512 would change the declared operating point and has no qualifying
latency measurement. The replacement is rejected. New-index selection and
normal benchmark coverage are removed. The version 3, 4, and 5 readers,
migration support, internal writer, focused tests, and research-quality sweep
remain.

### RaBitQ: Rejected, 2026-07-28

The same run measured recall@10 of 0.194 at 64 dimensions and 0.156 at 128
dimensions, against the declared minimum of 0.90. It took 18.2 ms and 40.5 ms
and allocated 10.0 MB and 20.6 MB per operation. New-index selection and normal
benchmark coverage were removed. The legacy decoder and internal research
fixtures remain only to support migration evidence.
