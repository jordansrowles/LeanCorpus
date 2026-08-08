---
adr: ADR020
title: Stop the Hybrid Retrieval 2.0 research branch
date: 2026-07-30
status: Accepted
version-added: vNext
summary: Stop the Hybrid Retrieval 2.0 research branch after failed ship gates.
areas: [search, vectors, hybrid-retrieval]
---

# ADR020: Stop the Hybrid Retrieval 2.0 research branch

- **Date:** 2026-07-30
- **Status:** Accepted

## Context

Hybrid Retrieval 2.0 expanded from vector correctness and HNSW planning into
experimental Matryoshka retrieval, RaBitQ, product quantisation, learned-sparse
retrieval, calibrated fusion, and late interaction.

The work produced useful correctness fixes, diagnostics, benchmarks, migration
coverage, planner evidence, and reference implementations. It also produced a
large unreleased change set with several unfinished public surfaces.

The three headline adaptive or compressed retrieval experiments did not pass
their ADR016 ship gates:

- Matryoshka failed latency and allocation gates by a wide margin.
- RaBitQ failed recall, latency, and allocation gates.
- Product quantisation passed storage and latency measures but failed
  128-dimensional default-budget recall.

Continuing the branch would commit more time to storage and retrieval
experiments instead of the higher-priority NLP work.

## Decision

Stop Hybrid Retrieval 2.0 development and return `vnext` to commit
`a839a340f`, `Update changelog`.

Preserve the complete unreleased implementation, tests, documentation, scripts,
and selected benchmark artefacts on the local `vnext-hybrid-failed` branch.
Keep the epic plan and tracker outside the repository under the user's control.

No work from the research branch is treated as shipped. Potentially valuable
parts may be reintroduced later as small, independently reviewed changes with a
clear product requirement.

## Rationale

The experimental failures are useful results, but they do not justify shipping
a large coupled change set. Retaining the entire branch preserves source-level
knowledge and allows future investigation without burdening `vnext` with
unstable APIs or formats.

A clean rollback is preferable to selectively retaining many intertwined
changes without another review cycle. Useful pieces such as vector lifecycle
hardening, planner corrections, fusion contracts, learned-sparse retrieval,
diagnostics, or prepared scorers can be evaluated separately if future NLP
work needs them.

## Consequences

- `vnext` returns to the known `a839a340f` baseline.
- `vnext-hybrid-failed` is the source and evidence archive.
- ADR017, ADR018, and ADR019 record the individual rejected experiments.
- Selected benchmark directories are committed despite the normal `bench/`
  ignore rule.
- Reintroduction requires a bounded change, current validation, and an explicit
  product use case.
- The external Hybrid Retrieval 2.0 plan and tracker are not copied into Git.
