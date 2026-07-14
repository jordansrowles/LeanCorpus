# ADR006: Defer Stryker.NET mutation testing until upstream bug is fixed

- **Date:** 2026-06-17
- **Status:** Accepted

## Context

Stryker.NET was evaluated as a mutation-testing tool for the LeanCorpus core library.
Installation, configuration, CI integration, and a runner script were prototyped. Stryker
4.14.2 discovered 74 tests against the core library and began mutating at Standard level.

## Problem

Every run failed with `Internal error due to compile error` in `CSharpRollbackProcess.Start`.
Stryker's Safe Mode correctly identified ~20 methods whose mutations produced compile errors
and rolled back each method individually. However the final post-rollback compilation still
failed fatally because of a cascade triggered by the .NET 10 SDK's implicit-using source
generator.

When Stryker mutates a source file and then rolls back a method, the file content changes.
The SDK's auto-generated `GlobalUsings.g.cs` contains `[InterceptsLocation]` attributes with
SHA-256 checksums of source file positions. After a rollback changes the file bytes, the
stale checksums in `GlobalUsings.g.cs` no longer match, producing CS9234 errors that the
rollback process cannot attribute to any source mutation.

This is tracked upstream as [stryker-net#3594](https://github.com/stryker-mutator/stryker-net/issues/3594)
(opened 2026-05-19, still open). The proposed fix — constructing semantic models from the
full compilation syntax trees rather than user source files only — has not been merged.

## Decision

Stryker.NET is removed from the repository. The evaluation artefacts (config, scripts, CI
steps) are deleted. Mutation testing is deferred until a Stryker release includes the
#3594 fix.

## Consequences

- The `CHAOS_ITERATIONS` env-var approach and the new fuzz test files from the evaluation
  are retained. They increase FsCheck coverage independently.
- When Stryker ships a fix for #3594, re-adoption is straightforward: the config shape and
  CI pattern were validated; only the tool manifest, config file, and CI steps need to be
  reinstated.
- Mutation testing remains a gap. For now, the property-based chaos suite (29 FsCheck
  properties at 1000 iterations in CI) provides the primary coverage-adequacy signal.
