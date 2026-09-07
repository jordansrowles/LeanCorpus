---
adr: ADR030
title: "DevOps owns unified artefact and test evidence"
date: 2026-09-07
status: Accepted
version-added: vNext
summary: "Build, test, coverage, benchmark, diagnostics, package and documentation output share one artefact root and provenance contract."
areas: [devops, testing, telemetry, benchmarks]
---

# ADR030: DevOps owns unified artefact and test evidence

- **Date:** 2026-09-07
- **Status:** Accepted

## Context

LeanCorpus development workflows historically wrote generated output to several
unrelated repository directories. Test results, diagnostics, coverage and
benchmark measurements used different run identities and provenance formats.
This made failed or interrupted work difficult to correlate and forced CI and
documentation tooling to know subsystem-specific paths.

xUnit 4 and Microsoft Testing Platform v2 provide test-level parallelisation,
explicit-test control, warnings, structured reports, GitHub reporting and a
stable Native AOT runner. LeanCorpus already exposes BCL activities and meters,
but test execution did not associate those signals with an individual test.

## Decision

The .NET SDK `ArtifactsPath` is the sole root for build, intermediate, package
and publish output. DevOps extends that same `artifacts/` root for test,
coverage, benchmark, diagnostics, documentation and temporary output.

Every run-capable DevOps subsystem writes a common `run.json` envelope when it
starts and completes it without replacing Git, framework, operating-system or
machine provenance. DevOps supplies the run identity and target directory to
child processes through a small `LEANCORPUS_*` environment contract.

Normal test projects use xUnit 4 on MTP v2. A repository-owned configuration
sets conservative defaults. DevOps selects full test-case parallelism only for
unit and explicit stress profiles; integration remains collection-parallel and
existing collection-level global-state exclusions remain authoritative.
Explicit tests are excluded unless requested. Ordinary tests are never retried.

Diagnostic test processes install first-party `ActivityListener` and
`MeterListener` instances. Each test owns a `leancorpus.test` root activity and
is indexed by trace identity, preventing concurrent tests from receiving one
another's signals. Raw activity and metric records are streamed during the
test, summaries and warnings are attached through `TestContext`, and runtime
measurements remain at execution scope. Standard benchmark runs keep listeners
disabled so diagnostic overhead cannot contaminate normal measurements.

`./devops benchmark` owns one run containing the Core, Rowles.Text and
compression projects. A failure in one project does not remove earlier output.
Generated benchmark documentation discovers all three families from those run
directories.

## Rationale

The SDK already implements collision-safe project, configuration, framework and
RID output layouts, so reproducing that logic in PowerShell would create a
second source of truth. One run envelope makes local, CI and documentation
consumers independent of runner-specific default directories.

Direct BCL listeners preserve Native AOT compatibility and avoid depending on
the experimental MTP OpenTelemetry entry-point replacement. Trace identity is
the only safe correlation key once tests may execute concurrently.

## Consequences

- Generated workflow output belongs under `artifacts/`; legacy output paths are
  retained only as historical references.
- `scripts/devops/artifacts/` owns path resolution, atomic manifests and bounded
  cleaning. Full cleaning is explicit; default cleaning preserves packages,
  benchmark history and downloaded benchmark data.
- Test projects share linked diagnostics sources and the centrally managed
  GitHub Actions reporter dependency. This is test infrastructure and does not
  change shipped LeanCorpus or Rowles.Text package APIs.
- The Native AOT smoke project uses released xUnit 4 AOT and MTP v2 packages.
- Raw diagnostic telemetry is intentionally more expensive and is confined to
  diagnostic, flaky and stress profiles. CI retains summaries.
- `./devops pack` writes packages and a checksum manifest to
  `artifacts/package/release/`.
