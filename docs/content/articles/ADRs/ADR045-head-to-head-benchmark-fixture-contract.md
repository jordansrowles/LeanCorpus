---
adr: ADR045
title: "Head-to-head benchmark fixture contract"
date: 2026-11-09
status: Accepted
version-added: v4.0.0
summary: "Head-to-head benchmark fixtures use equal storage, record topology, prove output outside timing and change identity when the fixture changes."
areas: [performance, testing]
relates-to: [ADR030]
---

# ADR045: Head-to-head benchmark fixture contract

- **Date:** 2026-11-09
- **Status:** Accepted

## Context

Core benchmarks compare LeanCorpus with Lucene.NET on the same DataForge
corpus. Several fixtures did not compare like with like. Lucene.NET used
`RAMDirectory` in `range`, `parallel`, `indexsort-index` and `suggester`
while LeanCorpus used memory-mapped files; a matched probe showed that
favoured Lucene.NET on range queries by about 20 to 60%. Segment topology was
never recorded, although LeanCorpus built 10 segments where Lucene.NET built 3
or 6, and topology alone moved regexp and range timings by up to a factor of
four. Some rows did no work at all: `tv-highlighter` Lucene.NET rows
highlighted unstored, empty text, `multiphrase` matched nothing on either
engine, and most `terminset` terms were absent from the corpus. Every one of
these rows passed, because a terminal BenchmarkDotNet result was the only gate.

## Decision

Every head-to-head benchmark fixture follows this contract.

1. **Equal storage.** Both engines use memory-mapped file storage under
   `BenchmarkHelpers.TempRoot`. Lucene.NET uses `MMapDirectory`; in-memory
   directories are not used for head-to-head rows. A DevOps test fails when a
   head-to-head benchmark source constructs a different Lucene.NET directory
   type.
2. **Declared and recorded topology.** Each fixture records, per engine, its
   directory type, segment count and document count. The record is written
   to the benchmark log and as a JSON sidecar under the run artefact
   directory's `topology/` child, next to the DataForge sidecars of ADR030.
   Where topology is known to dominate a result, single-segment rows are kept
   alongside the as-shipped rows rather than replacing them.
3. **Output preflight outside timing.** Setup runs each case's actual query or
   input once per engine and fails when it returns zero hits or empty output.
   A case that is meant to return nothing declares it explicitly, as `fuzzy`
   does with `requiresNoMatch`.
4. **Logical-ID parity where a claim needs it.** When a row is used to compare
   result sets, parity is checked on logical document IDs, never on
   engine-internal document numbers.
5. **Analysis output parity.** Analysis comparisons check term, position
   increment, position length and offsets, with any accepted difference listed
   in an explicit allow-list.
6. **Changed fixture, new identity.** A change to a fixture's data, storage,
   topology or query is a new benchmark. Its first run proves that the case
   does real work; it is not a before-and-after performance result.

## Rationale

Unequal storage and undeclared topology produce differences that belong to
the fixture, not to either engine, so they must be removed or made visible
before any ratio means anything. A zero-hit or empty-output row measures
dictionary misses or object construction and looks like a fast result, so it
must fail rather than pass quietly. Checking output once in setup costs nothing
in the timed region. Recording topology as a sidecar keeps it with the run
evidence that ADR030 already defines, without changing the report schema.

## Consequences

- `range`, `parallel`, `indexsort-index` and `suggester` Lucene.NET rows now
  run on `MMapDirectory`; their earlier results are not comparable.
- `range` and `regexp` gain single-segment rows next to the as-shipped rows.
- Fixtures that do no work fail setup, and DataForge tests re-create each known
  defect to prove the gate fires.
- Topology sidecars are evidence only. Reports do not yet read them.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-09 | Accepted | Original decision recorded. | Sprint 3 PR 1 |
