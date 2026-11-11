---
adr: ADR047
title: "Qualification finalisation and campaign failure policy"
date: 2026-11-11
status: Accepted
version-added: v4.0.0
summary: "Benchmark run identity, per-pair qualification and run-level claim eligibility are assessed separately and finalised fail-closed; campaigns continue past case failures and still fail."
areas: [devops, benchmarks]
extends: [ADR030]
relates-to: [ADR045]
---

# ADR047: Qualification finalisation and campaign failure policy

- **Date:** 2026-11-11
- **Status:** Accepted

## Context

`./devops benchmark` writes run evidence under ADR030 and compares engines only
through reviewed case families under ADR045. The 5 November 2026 exhaustive run
(`aeb6dd77`) exposed defects that made qualification unreachable even for a
genuinely matched pair, and others that would have let an invalid pair through:

- The identity validator always added `ComparisonEvidencePending` and reported
  `Unqualified`, and the run report published that provisional result. The
  separate pair assessment was never reconciled with it, so no run could become
  qualified.
- The pair assessor compared the BenchmarkDotNet informational version, which
  carries a `+commit` build suffix, with the bare package version, and compared
  each benchmark child's runtime with the .NET 10 orchestrator's runtime.
- `preflightPassed` was true whenever BenchmarkDotNet produced a terminal
  measurement. A method returning a wrong result set still passed.
- A Text-only run could never validate, because identity required a Core report.

The four reviewed `QualifiedParity` groups (collapse, facet offset page, queued
deletes and committed deletes) are defined at 20,000 documents. Exhaustive runs
stop most rows at 12 to 15 samples, below the 30-sample gate. Identical work
differs by up to about 9% between benchmark processes on the reference host.

## Decision

**Three separate assessments.** A run records:

1. **Execution status:** whether every planned case ran to a terminal result.
2. **Identity assessment:** source, host, dataset, toolchain and report
   integrity, `Validated` or `Rejected`. It no longer says anything about
   comparisons.
3. **Semantic comparison assessment:** one entry per reviewed pair with a
   per-pair state.

A deterministic **finalisation** step derives the published qualification from
those three, and nothing else:

- A pair is `Qualified` only when identity is `Validated` and the pair has no
  reason code. It is `Directional` when identity is `Validated`, its semantic
  proofs passed and it fails only the sample-count or variability gates. Every
  other pair is `Unqualified`. Each pair keeps its reason codes.
- The run is `Qualified` only when identity is `Validated`, execution completed
  and at least one pair is `Qualified`. Its scope is `all-pairs` when every
  assessed pair qualified, otherwise `qualified-pairs-only`. A ratio may be
  published only for a `Qualified` pair inside a `Qualified` run.
- Missing evidence never qualifies: no pairs, a rejected identity, an
  incomplete execution, a missing proof or a missing row all finalise as
  `Unqualified` with a named reason.

**Semantic proof.** A row qualifies only with a versioned semantic proof bound
to its case: benchmark class, methods, parameters, the dataset content hash and
the source commit. The proof records a fixture identifier, a proof identifier,
an output digest, pass or fail and reasons. Method execution success is a
separate field. A stale proof (another commit), a proof for another dataset or
case, or a failed proof fails closed. Core benchmark report schema v6 carries
the proof. Schema v4 and v5 reports remain readable and are always unqualified.

**Toolchain and runtime identity.** One version policy accepts BenchmarkDotNet
`0.16.0-preview.2`, with or without a `+` build suffix, keeps the original
string as provenance, and rejects every other version. Machine, operating
system, runtime identifier and architecture are compared with the orchestrator.
Child runtime, JIT, toolchain and job settings are compared between the two
paired children, never with the orchestrator, so a .NET 10 host may run .NET 11
children, while a .NET 10 child is never paired with a .NET 11 child.

**Statistical gates.** At least 30 workload samples and at most 5% coefficient
of variation per engine. These values do not change to make a run pass. The
four 20,000-document groups are qualified with a forced iteration count
(`-- --iterationCount 30`); `--strat exhaustive` is unchanged and its rows are
expected not to qualify. The minimum launch count is 1. Between-launch noise is
handled by forbidding any claim of a difference under 10%.

**Publication scope.** LeanCorpus 4.0 publishes no head-to-head ratio except
pairs that qualify under this ADR. No Text or 100,000-document case families
are added by this decision, and Text exclusions stay.

**Canonical reports per project.** Every project that writes a canonical
`report.json` (Core and Rowles.Text) is expected to produce one per invocation,
so a Text-only run can reach `Validated`.

**Campaign failure policy.** A campaign continues after a case-level or child
failure and records it; the aggregate result stays `Failed`. It stops at once on
a build failure, a timeout or cancellation. Continuing never improves the
outcome.

## Rationale

Keeping identity, comparison and the final decision separate lets each fail for
its own reason and makes the final state reproducible from evidence already in
the run directory. Binding proof to the measured case, dataset and commit closes
the gap where a fast wrong answer looked valid. Comparing children with each
other, not with the orchestrator, matches how BenchmarkDotNet launches work.
Holding the gates fixed keeps the decision honest: a pair qualifies because it
met a pre-registered bar, not because the bar moved.

## Consequences

- Run reports gain `identityAssessment`, `semanticComparisonAssessment` and a
  finalised `qualification`, with reason codes at each level.
- Most exhaustive rows remain `Unqualified` or `Directional`. The final 4.0 run
  is a comparison with the first run, not a qualification.
- Reports from before schema v6, including the 5 November run, stay
  unqualified.
- New case families need a semantic proof wired into their benchmark class
  before they can qualify.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-11 | Accepted | Original decision recorded. | Sprint 5 PR 1 |
