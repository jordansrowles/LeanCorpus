### Added

* Added unified DevOps run manifests, xUnit-correlated test telemetry, runtime diagnostics, CTRF and GitHub Actions reporting, and safe `clean` and package-manifest workflows under `artifacts/`.

### Changed

* Strengthened architecture tests for exact package and plugin isolation, Server transport boundaries, filesystem ownership, and Native AOT runtime IL-generation boundaries.
* Bumped the LeanCorpus package to 3.1.1 for the stored-field persistence fix.
* Consolidated build, test, coverage, benchmark, diagnostic and documentation output under the SDK artefact root; normal benchmark runs now cover Core, Rowles.Text and compression in one provenance-scoped run, and Native AOT smoke tests use the stable xUnit 4 MTP v2 stack.

### Fixed

* Cleared pooled stored-field writer scratch before use so previous search activity cannot silently omit fields from newly written segments.
* Isolated stress-test metric listeners and statistics temporary-file checks from concurrent test activity.
* Hardened Windows lifecycle tests and made repeated MTP test diagnostics aggregate by semantic test identity across executions.
* Fixed CI-prepared test executable discovery, empty-telemetry reporting and central-output metadata tests.
* Fixed benchmark report schema collisions, result-based benchmark exit codes, coverage status propagation, current-commit documentation coverage, and interrupted artefact-run finalisation.
* Hardened benchmark documentation evidence selection, dirty-worktree coverage provenance, shared coverage/test run identity, best-effort test telemetry, and Windows stress-test diagnostics.

### Removed

### Deprecated

### Security
