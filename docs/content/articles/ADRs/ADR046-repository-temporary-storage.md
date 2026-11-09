---
adr: ADR046
title: "Repository temporary storage"
date: 2026-11-09
status: Accepted
version-added: v4.0.0
summary: "Repository-owned tests, benchmarks and tooling create temporary files under the checkout's artifacts/tmp, owner-only and unique, enforced by an architecture rule."
areas: [testing, reliability]
relates-to: [ADR030, ADR045]
---

# ADR046: Repository temporary storage

- **Date:** 2026-11-09
- **Status:** Accepted

## Context

About 270 source files in the repository's tests, benchmarks, DataForge, Server
tests and DevOps tooling chose the operating system's temporary directory with
`Path.GetTempPath()`. On Linux that is the world-writable `/tmp`. SonarQube
reports these as S5443 and S5445: another local user or process can pre-create
or replace a predictable path. The checkout already has an ignored `artifacts/`
root (ADR030), and benchmarks already used a private `artifacts/temp/benchmark`.
DevOps.Tests alone declared about 20 private temporary-directory classes with
slightly different clean-up behaviour.

## Decision

Repository-owned code allocates temporary storage under
`<repository root>/artifacts/tmp/<area>/<prefix><32 hex>`.

- **Areas** are a fixed list: `tests`, `chaos`, `benchmarks`, `profiling`,
  `dataforge`, `server-tests`, `aot`, `devops`, `devops-tests`, `cli`.
- **Repository root** is the nearest ancestor of `AppContext.BaseDirectory`
  containing `.config/devops/test-suites.json`. If none exists, creation throws;
  it never falls back to operating-system temporary storage. The DevOps tool
  uses its existing `ArtifactPaths` root (`ArtifactPaths.TemporaryRoot`,
  `artifacts/tmp/devops`) because it runs against an explicit `--repo`.
- **Optional override.** `LEANCORPUS_TEMP_ROOT` can replace
  `<repository root>/artifacts/tmp` as the parent of the area directories, for
  example to keep fsync-heavy tests on a developer's RAM disk. It must already
  exist, must not be a symbolic link and, on Unix, must have no group or other
  permission bits; otherwise creation throws. A writable owner-only directory
  belongs to the current user (or the user is root), so the shared-directory
  exposure does not return. The override is never inferred from `TMP`, `TEMP` or
  `TMPDIR`.
- **Creation** uses a new GUID per directory. On Unix the directory is created
  owner-only (read, write and execute for the user). Directories are never
  shared between tests or processes.
- **Clean-up** deletes on dispose, retrying `IOException` and
  `UnauthorizedAccessException` for up to five seconds while Windows releases
  handles held by terminated child processes, then rethrows. It deletes only
  the directory it created.
- **One helper,** `RepositoryTemporaryStorage` in
  `Rowles.LeanCorpus.Tests.Shared/Fixtures`, has no dependency on any
  LeanCorpus assembly. Projects that do not reference Tests.Shared link the file
  as source, as Tests.Core already does for `TestDirectoryFixture`.
- **Architecture rule.** `TemporaryStorageConventionTests` parses every tracked
  C# file under `src/devops/` and `src/server/devops/`. It rejects calls to
  `Path.GetTempPath`, `Path.GetTempFileName` and
  `Directory.CreateTempSubdirectory`, including qualified, aliased and
  `using static` forms, and reads of the `TMP`, `TEMP` and `TMPDIR` environment
  variables. The only allowed exceptions are registered by file and member,
  with a reason, in one of four categories:
  - **A,** cross-checkout coordination (the benchmark host lease);
  - **B,** a test that must create a checkout, worktree or symlink outside the
    repository;
  - **C,** a test whose subject is operating-system temporary storage itself;
  - **D,** the helper's own root resolution.
  Stale register entries also fail the rule.

Consumer code is outside this decision. `PackedBkdWriter` and
`PackedBkdBuilder` resolve spill storage as: an explicitly configured spill
directory, then a directory adjacent to the output, then operating-system
temporary storage. Consumers have no `artifacts/` directory, and application
data suits persistent state rather than large disposable spill files. If that
fallback is revisited, `Directory.CreateTempSubdirectory()` (owner-only on Unix)
is the preferred API.

## Rationale

The checkout is a trusted, user-owned location, so moving repository-owned
temporary files there removes the shared-directory exposure. Owner-only,
unique creation keeps the protection when a checkout lives on a shared host.
A single helper replaces many divergent copies and gives every test the same
Windows-safe clean-up. A source-level rule is needed because the calls are
spread across test projects that an assembly-dependency rule does not cover,
and the explicit register keeps the genuine exceptions reviewable.

## Consequences

- Test, benchmark and tooling temporary files appear under `artifacts/tmp`,
  which is already ignored by Git. Benchmark indexes move from
  `artifacts/temp/benchmark` to `artifacts/tmp/benchmarks` on the same
  filesystem; under ADR045 this is a fixture-location change that does not
  alter measured I/O.
- New code that selects operating-system temporary storage in these projects
  fails the architecture tests unless it is registered with a reason.
- Local test I/O moves off any RAM-disk `/tmp`. On a host where `/tmp` is tmpfs,
  durable-commit tests can be several times slower on disk; the backup
  state-machine test went from 17 seconds to beyond the 100-second hang timeout.
  `LEANCORPUS_TEMP_ROOT` restores RAM-disk speed without restoring shared paths.
  Hosted CI runners already use disk-backed temporary storage.
- Literal `/tmp` strings, which are mostly test inputs, are not covered.
- Library code, including the Server libraries and `src/examples`, is
  unchanged.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-09 | Accepted | Original decision recorded. | Sprint 3 PR 3 |
