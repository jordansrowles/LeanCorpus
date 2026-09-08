### Added

* Added unified DevOps run manifests, xUnit-correlated test telemetry, runtime diagnostics, CTRF and GitHub Actions reporting, and safe `clean` and package-manifest workflows under `artifacts/`.

### Changed

* Consolidated build, test, coverage, benchmark, diagnostic and documentation output under the SDK artefact root; normal benchmark runs now cover Core, Rowles.Text and compression in one provenance-scoped run, and Native AOT smoke tests use the stable xUnit 4 MTP v2 stack.

### Fixed

* Hardened Windows lifecycle tests and made repeated MTP test diagnostics aggregate by semantic test identity across executions.
* Fixed CI-prepared test executable discovery, empty-telemetry reporting and central-output metadata tests.

### Removed

### Deprecated

### Security
