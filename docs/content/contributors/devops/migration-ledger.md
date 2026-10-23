# Managed DevOps migration ledger

> Historical #11 baseline `4ccdf1ba7ae447ed982a34dc64478df47879baf3` on `issue-97`; the latest local runtime-tested source tree is dirty and based on pushed commit `3718a8c5704ff020b11ebcc68ba2dff658916f4c`. The Phase 7 workbench and docs evidence below was collected from that tree. The active scope is the full issue #8 migration across Phases 0 through 9. First-milestone evidence remains valid; later phase gates are tracked as they are implemented and qualified.

## Source and generated-document counts

The pre-#11 snapshot (`7e5bf521d48da2df20a5bc7af1ec816022d4e9ce`) contained **445 tracked paths under `docs/`, including 274 Markdown files**. The generated `docs/benchmarks/` subtree accounted for **146 paths, including 76 Markdown pages**. Removing that subtree from the count leaves 299 paths and 198 Markdown files across the remaining mixed documentation, assets, configuration and templates; that remainder is not an authored-Markdown count.

The post-#11 tracked tree (`4ccdf1ba7ae447ed982a34dc64478df47879baf3`) contains **272 paths, including 177 Markdown files**. Canonical `docs/content/` contains 179 paths, including 177 Markdown files; `docs/site-config/` contains 93 non-Markdown files. Generated API, benchmark, coverage and site output is staged beneath ignored `artifacts/docs/` and is not part of these current source-tree counts.

At post-pull revision `4244928c4539e986dedaa974b6045b27f8435be3`, the tree contains **276 paths, including 180 Markdown files**. Four DevOps migration records were added under `docs/content/` after the post-#11 snapshot. At clean candidate `a37cb13e0a79f33a2550fdb22a8950d1a77b79db`, the same 276 / 180 count remains. The old generated `docs/benchmarks/` subtree is absent; the only two benchmark-named tracked paths are authored guides. Generated benchmark output remains under ignored `artifacts/docs/`.

## Environment

| Item | Baseline |
|---|---|
| Revision | `4ccdf1ba7ae447ed982a34dc64478df47879baf3` |
| Branch | `issue-97` |
| OS / RID | Debian GNU/Linux 13 (trixie), `linux-x64` |
| SDK selected | `11.0.100-preview.7.26381.103` |
| Other installed SDK | `10.0.103`, `11.0.100-preview.6.26359.118` |
| Runtimes | .NET 10.0.3; .NET 11 preview.6 and preview.7 |
| MSBuild | `18.10.0.38203` |
| PowerShell / Git | pwsh 7.5.4 / Git 2.47.3 |

## Commands and phase ownership

| Command | Legacy source | Planned phase | Current state |
|---|---|---:|---|
| `build` (`build`) | `scripts/devops/commands/build.ps1` | 1 | Managed BuildService is contract-tested and routed through the Linux-x64 apphost. Build, stress and diagnostics workflows now invoke the checked-out root apphost directly for product builds after setup; exact-SHA hosted verification is pending. |
| `test` (`test`, `test affected`, `test backup-restore-matrix`) | `scripts/devops/commands/test.ps1` | 5 | Managed MTP/AOT execution services and the internal five-cell backup/restore matrix are implemented and qualified on Linux; the public route remains closed pending the full §18 parity and stress gates. |
| `aot` (`aot`) | `scripts/devops/commands/aot.ps1` | 5 | Inventoried; not routed |
| `coverage` (`coverage`) | `scripts/devops/commands/coverage.ps1` | 6 | Managed test pipeline and pinned ReportGenerator route through the apphost; clean-source Linux `all` coverage passed for Core and SourceGen on net10.0 with two Cobertura reports and HTML output. Net11, Windows/macOS and hosted parity remain open. |
| `diagnostics` (`diagnostics ps`, `diagnostics counters`, `diagnostics trace`, `diagnostics gcdump`, `diagnostics dump`, `diagnostics symbols`, `diagnostics capture`) | `scripts/devops/commands/diagnostics.ps1` | 4 | All seven variants route through the managed apphost. Fake-runner lifecycle tests and a bounded Linux capture pass; live attach parity on other platforms remains open. |
| `benchmark` (`benchmark`, `benchmark affected`, `benchmark remote start/status/cancel`) | `scripts/devops/commands/benchmark.ps1` | 4 | Local suites, changed-path group selection and SSH/tmux lifecycle route through the managed apphost. The manual workflow's Core dry-run passed through the checked-out host after bootstrap on SHA `6c610fd57` (Build `39879445230`); real measurements, live remote and non-Linux parity remain open. |
| `data` (`data gutenberg`, `data migration-fixtures-311`) | `scripts/devops/commands/data.ps1` | 3 | Inventoried; not routed |
| `dataforge` (`dataforge profiles`, `dataforge generate`, `dataforge inspect`, `dataforge verify`, `dataforge reproduce`, `dataforge reference download`, `dataforge reference build`, `dataforge reference inspect`, `dataforge reference verify`, `dataforge qualify`) | `scripts/devops/commands/dataforge.ps1` | 3 | `dataforge qualify` is implemented and routed through the managed apphost; other variants remain on PowerShell. Linux qualification passed; cross-platform parity remains open. |
| `docs` (`docs prepare`, `docs build`, `docs metadata`, `docs serve`, `docs test`) | `scripts/devops/commands/docs.ps1` | 6 | Managed documentation services implement and route all five actions through `./devops`; Linux output parity, static checks and the 11-case browser suite pass. The PowerShell implementation remains as rollback; hosted current-SHA validation is pending. |
| `benchmarks` (`benchmarks docs`) | `scripts/devops/commands/benchmarks.ps1` | 4 | Inventoried; not routed |
| `setup` (`setup`) | `scripts/devops/commands/setup.ps1` | 0 | Inventoried; not routed |
| `clean` (`clean [target]`) | `scripts/devops/commands/clean.ps1` | 1 | Managed CleanService is tested on disposable fixtures and routed through the Linux-x64 apphost. |
| `pack` (`pack`) | `scripts/devops/commands/pack.ps1` | 3 | Inventoried; not routed |
| `ui` (`ui`) | `scripts/devops/commands/ui.ps1` | 7 | Managed Spectre.Console workbench is implemented; Linux tests and PTY acceptance cover narrow-width resize/navigation, Ctrl+C exit 130 and terminal restoration. Historical-route parity, focus and Windows/macOS/hosted acceptance remain open. |
| `report` (`report git`, `report files`, `report code`) | `scripts/devops/commands/report.ps1` | 2 | Linux managed all-groups report passed with 50/50 child exits and 37 evaluated projects; promoted-host PowerShell forwarding passed with 8/8 child exits. Report tests cover versioned JSON, strict checks, unusual paths, rename-aware churn, shallow history and syntax-analysis limits. Windows/macOS and hosted parity gates remain open. |
| `server` (`server start`) | `scripts/devops/commands/server.ps1` | 1 | Managed foreground server is tested and routed through the Linux-x64 apphost. |

The PowerShell transition dispatcher currently forwards `build`, `clean`, `report`, `coverage`, `benchmark`, `diagnostics`, `ui`, and foreground `server start`. The Build, stress and diagnostics workflows now invoke the checked-out root apphost directly for product builds after setup. The managed coverage host preserves registry eligibility and the `Coverage!=Skip` default, links its test run, and retains raw Cobertura plus optional ReportGenerator HTML output. Clean-source Linux coverage for both eligible suites passed on net10.0 in run `a81b4160-9800-4a18-a549-96eca9eeb0df`; Net11, Windows/macOS and hosted parity remain open. The managed benchmark host runs local suites, selects affected benchmark classes from staged, modified, renamed and untracked paths, and manages remote SSH/tmux start, status and cancellation. Remote cancellation remains uncertain until a terminal status is returned. The manual benchmark workflow's Core dry-run passed through the checked-out host after setup on SHA `6c610fd57` (Build `39879445230`); real measurements, benchmark documentation, live remote use and non-Linux parity remain open. `test` and `aot` remain on PowerShell until their complete Phase 5 slice. Every other command remains on its current path until its own phase and parity gate.

## Legacy module inventory

75 tracked files under `scripts/devops/` were inventoried. Each source file is individually listed below; common and artifact modules remain shared dependencies until every consuming command has migrated.

| ID | Source path | Owner | Phase | Callers/state |
|---|---|---|---:|---|
| `SRC-001` | `scripts/devops/DevOps.psm1` | `composition:devops-psm1` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-002` | `scripts/devops/artifacts/clean.ps1` | `shared:artifacts` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-003` | `scripts/devops/artifacts/manifests.ps1` | `shared:artifacts` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-004` | `scripts/devops/artifacts/paths.ps1` | `shared:artifacts` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-005` | `scripts/devops/artifacts/runs.ps1` | `shared:artifacts` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-006` | `scripts/devops/commands/aot.ps1` | `command:aot` | 5 | CALL-002, CALL-005, CALL-006, CALL-007, CALL-008, CALL-015, CALL-018, CALL-019, CALL-032, CALL-033, CALL-040; inventoried |
| `SRC-007` | `scripts/devops/commands/benchmark.ps1` | `command:benchmark` | 4 | CALL-001, CALL-005, CALL-008, CALL-010, CALL-013, CALL-015, CALL-016, CALL-025, CALL-026, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-036, CALL-039; inventoried |
| `SRC-008` | `scripts/devops/commands/benchmarks.ps1` | `command:benchmarks` | 4 | CALL-008, CALL-015, CALL-028, CALL-033, CALL-036, CALL-039; inventoried |
| `SRC-009` | `scripts/devops/commands/build.ps1` | `command:build` | 1 | CALL-002, CALL-003, CALL-004, CALL-005, CALL-008, CALL-015, CALL-016, CALL-017, CALL-018, CALL-020, CALL-023, CALL-025, CALL-026, CALL-032, CALL-033, CALL-034, CALL-037, CALL-041; inventoried |
| `SRC-010` | `scripts/devops/commands/clean.ps1` | `command:clean` | 1 | CALL-002, CALL-015, CALL-016, CALL-032, CALL-033; inventoried |
| `SRC-011` | `scripts/devops/commands/coverage.ps1` | `command:coverage` | 6 | CALL-002, CALL-007, CALL-008, CALL-015, CALL-032, CALL-033; legacy source retained pending Phase 8 retirement |
| `SRC-012` | `scripts/devops/commands/data.ps1` | `command:data` | 3 | CALL-008, CALL-014, CALL-015, CALL-016, CALL-021, CALL-038; inventoried |
| `SRC-013` | `scripts/devops/commands/dataforge.ps1` | `command:dataforge` | 3 | CALL-008, CALL-016, CALL-025, CALL-026, CALL-034, CALL-035, CALL-044; inventoried |
| `SRC-014` | `scripts/devops/commands/diagnostics.ps1` | `command:diagnostics` | 4 | CALL-003, CALL-032, CALL-033, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-015` | `scripts/devops/commands/docs.ps1` | `command:docs` | 6 | CALL-002, CALL-005, CALL-008, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-032, CALL-033, CALL-037; inventoried |
| `SRC-016` | `scripts/devops/commands/pack.ps1` | `command:pack` | 3 | CALL-002, CALL-010, CALL-015, CALL-016; inventoried |
| `SRC-017` | `scripts/devops/commands/report.ps1` | `command:report` | 2 | CALL-015; inventoried |
| `SRC-018` | `scripts/devops/commands/server.ps1` | `command:server` | 1 | CALL-008, CALL-015, CALL-016, CALL-022, CALL-023, CALL-024, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-019` | `scripts/devops/commands/setup.ps1` | `command:setup` | 0 | CALL-005, CALL-016, CALL-018; inventoried |
| `SRC-020` | `scripts/devops/commands/test.ps1` | `command:test` | 5 | CALL-002, CALL-003, CALL-004, CALL-005, CALL-007, CALL-008, CALL-010, CALL-011, CALL-012, CALL-015, CALL-016, CALL-018, CALL-019, CALL-023, CALL-024, CALL-027, CALL-030, CALL-032, CALL-033; inventoried |
| `SRC-021` | `scripts/devops/common/arguments.ps1` | `shared:common` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-022` | `scripts/devops/common/output.ps1` | `shared:common` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-023` | `scripts/devops/common/paths.ps1` | `shared:common` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-024` | `scripts/devops/common/processes.ps1` | `shared:common` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-025` | `scripts/devops/common/tools.ps1` | `shared:common` | 0 | CALL-001, CALL-002, CALL-003, CALL-004, CALL-005, CALL-006, CALL-007, CALL-008, CALL-009, CALL-010, CALL-011, CALL-012, CALL-013, CALL-014, CALL-015, CALL-016, CALL-017, CALL-018, CALL-019, CALL-020, CALL-021, CALL-022, CALL-023, CALL-024, CALL-025, CALL-026, CALL-027, CALL-028, CALL-029, CALL-030, CALL-031, CALL-032, CALL-033, CALL-034, CALL-035, CALL-036, CALL-037, CALL-038, CALL-039, CALL-040, CALL-041, CALL-042, CALL-043, CALL-044; inventoried |
| `SRC-026` | `scripts/devops/config/benchmark-datasets.psd1` | `config:benchmark` | 4 | See command/workflow inventory; inventoried |
| `SRC-027` | `scripts/devops/config/benchmark-groups.psd1` | `config:benchmark` | 4 | See command/workflow inventory; inventoried |
| `SRC-028` | `scripts/devops/config/benchmark-strategies.psd1` | `config:benchmark` | 4 | See command/workflow inventory; inventoried |
| `SRC-029` | `scripts/devops/config/benchmark-suites.psd1` | `config:benchmark` | 4 | See command/workflow inventory; inventoried |
| `SRC-030` | `scripts/devops/config/code-areas.psd1` | `config:code-areas` | 5 | See command/workflow inventory; inventoried |
| `SRC-031` | `scripts/devops/config/test-suites.psd1` | `config:testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-032` | `scripts/devops/data/fixtures-311/Generator.csproj.template` | `data:migration-fixtures` | 3 | See command/workflow inventory; inventoried |
| `SRC-033` | `scripts/devops/data/fixtures-311/Program.cs.template` | `data:migration-fixtures` | 3 | See command/workflow inventory; inventoried |
| `SRC-034` | `scripts/devops/data/fixtures-311/global.json.template` | `data:migration-fixtures` | 3 | See command/workflow inventory; inventoried |
| `SRC-035` | `scripts/devops/data/generate-migration-fixtures-311.ps1` | `data:migration-fixtures` | 3 | See command/workflow inventory; inventoried |
| `SRC-036` | `scripts/devops/diagnostics/capture.ps1` | `diagnostics` | 4 | See command/workflow inventory; inventoried |
| `SRC-037` | `scripts/devops/diagnostics/tools.ps1` | `diagnostics` | 4 | See command/workflow inventory; inventoried |
| `SRC-038` | `scripts/devops/packaging/Consumer.csproj.template` | `packaging` | 3 | See command/workflow inventory; inventoried |
| `SRC-039` | `scripts/devops/packaging/Program.cs.template` | `packaging` | 3 | See command/workflow inventory; inventoried |
| `SRC-040` | `scripts/devops/packaging/package-policy.json` | `packaging` | 3 | See command/workflow inventory; inventoried |
| `SRC-041` | `scripts/devops/packaging/validate-compression-package-archive.ps1` | `packaging` | 3 | See command/workflow inventory; inventoried |
| `SRC-042` | `scripts/devops/packaging/validate-compression.ps1` | `packaging` | 3 | See command/workflow inventory; inventoried |
| `SRC-043` | `scripts/devops/packaging/validate-package-archive.ps1` | `packaging` | 3 | See command/workflow inventory; inventoried |
| `SRC-044` | `scripts/devops/packaging/validate-package-consumers.ps1` | `packaging` | 3 | See command/workflow inventory; inventoried |
| `SRC-045` | `scripts/devops/support/benchmark.ps1` | `support:benchmark` | 4 | See command/workflow inventory; inventoried |
| `SRC-046` | `scripts/devops/support/coverage.ps1` | `support:coverage` | 6 | See command/workflow inventory; inventoried |
| `SRC-047` | `scripts/devops/support/docfx.ps1` | `support:docfx` | 6 | See command/workflow inventory; inventoried |
| `SRC-048` | `scripts/devops/testing/artifacts.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-049` | `scripts/devops/testing/backup-restore-matrix.ps1` | `testing` | 5 | Replaced internally by `BackupRestoreMatrixService` and `BackupRestoreSourceManifestReader`; five Linux net10.0 variants passed with matched source/workload/binary evidence, while public command routing stays closed. |
| `SRC-050` | `scripts/devops/testing/events.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-051` | `scripts/devops/testing/execution.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-052` | `scripts/devops/testing/preparation.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-053` | `scripts/devops/testing/reports.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-054` | `scripts/devops/testing/results.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-055` | `scripts/devops/testing/runner.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-056` | `scripts/devops/testing/targets.ps1` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-057` | `scripts/devops/testing/testconfig.json` | `testing` | 5 | See command/workflow inventory; inventoried |
| `SRC-058` | `scripts/devops/ui/components/commands.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-059` | `scripts/devops/ui/dependencies.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-060` | `scripts/devops/ui/pages/benchmarks.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-061` | `scripts/devops/ui/pages/dataforge.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-062` | `scripts/devops/ui/pages/observe.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-063` | `scripts/devops/ui/pages/surface.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-064` | `scripts/devops/ui/pages/tests.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-065` | `scripts/devops/ui/ui.ps1` | `ui` | 7 | See command/workflow inventory; inventoried |
| `SRC-066` | `scripts/devops/validation/adr-index-smoke.ps1` | `validation-oracle:adr-index-smoke` | 6 | See command/workflow inventory; inventoried |
| `SRC-067` | `scripts/devops/validation/affected-selection-smoke.ps1` | `validation-oracle:affected-selection-smoke` | 5 | See command/workflow inventory; inventoried |
| `SRC-068` | `scripts/devops/validation/artifacts-smoke.ps1` | `validation-oracle:artifacts-smoke` | 0 | See command/workflow inventory; inventoried |
| `SRC-069` | `scripts/devops/validation/backup-restore-matrix-smoke.ps1` | `validation-oracle:backup-restore-matrix-smoke` | 5 | See command/workflow inventory; inventoried |
| `SRC-070` | `scripts/devops/validation/docs-site-smoke.ps1` | `validation-oracle:docs-site-smoke` | 6 | See command/workflow inventory; inventoried |
| `SRC-071` | `scripts/devops/validation/runtime-identifier-smoke.ps1` | `validation-oracle:runtime-identifier-smoke` | 5 | See command/workflow inventory; inventoried |
| `SRC-072` | `scripts/devops/validation/test-progress-smoke.ps1` | `validation-oracle:test-progress-smoke` | 5 | See command/workflow inventory; inventoried |
| `SRC-073` | `scripts/devops/validation/test-selection-smoke.ps1` | `validation-oracle:test-selection-smoke` | 5 | See command/workflow inventory; inventoried |
| `SRC-074` | `scripts/devops/validation/ui-contract-smoke.ps1` | `validation-oracle:ui-contract-smoke` | 7 | See command/workflow inventory; inventoried |
| `SRC-075` | `scripts/devops/validation/ui-smoke.ps1` | `validation-oracle:ui-smoke` | 7 | See command/workflow inventory; inventoried |

## Root launchers and bootstrap

| Path | Role | Milestone decision |
|---|---|---|
| `devops` | Ignored generated current-RID managed apphost | The tracked Bash wrapper has been removed from the index after Linux promotion and active Unix caller updates. `setup.ps1` regenerates this path. |
| `devops.ps1` | PowerShell transition dispatcher | Forwards proven `build`, `clean`, `report` and `server start` calls to a fingerprint-validated host; unported commands remain on PowerShell and never fall back after a managed failure. |
| `setup.ps1` | Checkout bootstrap | Preserves the pinned DocFX restore, then locked-restores/builds/tests/publishes, probes and atomically promotes the one-file apphost with bundled native libraries. Linux-x64 setup passed; a later full-suite rerun passed 92/92. |

## External invocation inventory

44 tracked files outside `scripts/devops/` matched launcher or legacy module patterns in the baseline. Active Unix calls for commands not yet migrated now invoke `pwsh ./devops.ps1`; managed build/clean/server calls use the apphost. Dated evidence, release history and the captured legacy help fixture retain their original command strings. The three Server test project matches are relative source paths containing `devops`, not launcher invocations.

| ID | Path | Kind | Matches |
|---|---|---|---:|
| `CALL-001` | `.github/workflows/benchmark.yml` | workflow | 2 |
| `CALL-002` | `.github/workflows/build.yml` | workflow | 13 |
| `CALL-003` | `.github/workflows/test-diagnostics.yml` | workflow | 4 |
| `CALL-004` | `.github/workflows/test-stress.yml` | workflow | 4 |
| `CALL-005` | `CONTRIBUTING.md` | documentation | 18 |
| `CALL-006` | `README.md` | documentation | 1 |
| `CALL-007` | `changelog/3.0.0 - 2026-08-25.md` | source-message-or-example | 1 |
| `CALL-008` | `devops` | source-message-or-example | 2 |
| `CALL-009` | `devops.ps1` | source-message-or-example | 1 |
| `CALL-010` | `docs/content/articles/ADRs/ADR030-unified-devops-artefact-and-test-evidence.md` | documentation | 2 |
| `CALL-011` | `docs/content/articles/testing/backup-restore-measurement.md` | documentation | 2 |
| `CALL-012` | `docs/content/articles/testing/backup-restore-results.md` | documentation | 1 |
| `CALL-013` | `docs/content/contributors/benchmarking.md` | documentation | 3 |
| `CALL-014` | `docs/content/contributors/codeckit/03-migrations.md` | documentation | 1 |
| `CALL-015` | `docs/content/contributors/devops-entry-point.md` | documentation | 24 |
| `CALL-016` | `docs/content/contributors/devops-ui.md` | documentation | 8 |
| `CALL-017` | `docs/content/contributors/documentation.md` | documentation | 1 |
| `CALL-018` | `docs/content/contributors/index.md` | documentation | 9 |
| `CALL-019` | `docs/content/getting-started/01-installation.md` | documentation | 1 |
| `CALL-020` | `docs/content/index-management/04-cli-checker.md` | documentation | 1 |
| `CALL-021` | `docs/content/index-management/11-codec-migration-3-0.md` | documentation | 1 |
| `CALL-022` | `docs/content/server/01-running.md` | documentation | 4 |
| `CALL-023` | `docs/content/server/evidence/orchard-core-search-provider.md` | documentation | 1 |
| `CALL-024` | `docs/content/server/orchard-core-search-provider.md` | documentation | 5 |
| `CALL-025` | `docs/content/tips/03-benchmarking.md` | documentation | 13 |
| `CALL-026` | `docs/content/tips/dataforge.md` | documentation | 11 |
| `CALL-027` | `lexicons/README.md` | documentation | 3 |
| `CALL-028` | `scripts/benchmarks/send-remote.ps1` | developer-script | 1 |
| `CALL-029` | `scripts/nightly-benchmark.sh` | developer-script | 1 |
| `CALL-030` | `src/core/Rowles.Text/CONTRIBUTING.md` | documentation | 7 |
| `CALL-031` | `src/core/Rowles.Text/README.md` | documentation | 2 |
| `CALL-032` | `src/devops/CONTRIBUTING.md` | documentation | 20 |
| `CALL-033` | `src/devops/README.md` | documentation | 36 |
| `CALL-034` | `src/devops/Rowles.DataForge.Tool/DataForgeCommandLine.cs` | source-message-or-example | 15 |
| `CALL-035` | `src/devops/Rowles.DataForge.Tool/WikipediaReferenceSource.cs` | source-message-or-example | 1 |
| `CALL-036` | `src/devops/Rowles.LeanCorpus.Benchmarks.Compression/Program.cs` | source-message-or-example | 1 |
| `CALL-037` | `src/devops/Rowles.LeanCorpus.Tests.Docs/DocsSiteAcceptanceTests.cs` | source-message-or-example | 1 |
| `CALL-038` | `src/devops/Rowles.LeanCorpus.Tests.Shared/Fixtures/Indexes/README.md` | documentation | 1 |
| `CALL-039` | `src/devops/Rowles.Text.Benchmarks/Program.cs` | source-message-or-example | 1 |
| `CALL-040` | `src/examples/README.md` | documentation | 1 |
| `CALL-041` | `src/server/README.md` | documentation | 3 |
| `CALL-042` | `src/server/devops/Rowles.LeanCorpus.Server.Abstractions.Tests/Rowles.LeanCorpus.Server.Abstractions.Tests.csproj` | source-message-or-example | 2 |
| `CALL-043` | `src/server/devops/Rowles.LeanCorpus.Server.Core.Tests/Rowles.LeanCorpus.Server.Core.Tests.csproj` | source-message-or-example | 2 |
| `CALL-044` | `src/server/devops/Rowles.LeanCorpus.Server.Integration.Tests/Rowles.LeanCorpus.Server.Integration.Tests.csproj` | source-message-or-example | 3 |

## CI workflows

| Workflow | Current state and migration note |
|---|---|
| `.github/workflows/benchmark.yml` | The manual Core dry-run passed after setup through the checked-out root apphost at SHA `6c610fd57` (Build `39879445230`); actual benchmark execution, live remote and non-Linux parity remain open. |
| `.github/workflows/build.yml` | Adds a Linux/Windows managed-host qualification matrix. Linux/Windows product build steps and coverage call the checked-out host directly after bootstrap; product test and package matrices remain at their current acceptance boundary. Hosted exact-SHA verification is pending. |
| `.github/workflows/deploy.yml` | Documentation artefact deployment remains unchanged and does not invoke the root launcher. |
| `.github/workflows/test-diagnostics.yml` | Linux and Windows jobs bootstrap the managed host and invoke it directly for product builds; test execution remains on PowerShell pending Phase 5 parity. Hosted exact-SHA verification is pending. |
| `.github/workflows/test-stress.yml` | Linux and Windows jobs bootstrap the managed host and invoke it directly for product builds; test execution remains on PowerShell pending Phase 5 parity. Hosted exact-SHA verification is pending. |

## Managed CLI foundation

The isolated `net10.0` DevOps project graph contains the managed host, its tests and the process-test child, with project-local output/intermediate paths and lock files under the separate `src/devops` package graph. The product solution and root central package metadata are unchanged. The first-milestone suite passed 77/77; the current full suite passes 162/162 with no skips. Linux `setup.ps1` passed locked restores, test-project graph build, pre-publish tests, single-file publish with bundled native libraries and no sidecars, staged help/version/build probes, atomic promotion and promoted help/version checks. The root host is executable and ignored; its SHA-256 and source fingerprint are in `artifacts/devops/managed-host.json`. Direct apphost builds and PowerShell-forwarded builds both passed. The existing `Rowles.LeanCorpus.Cli` project also builds through the managed project route.

`pwsh ./scripts/devops/validation/managed-bootstrap-smoke.ps1` passed 39 checks using isolated fake SDK and host commands. It covers the wrong-checkout and missing-SDK failures; tool, locked restore, build and publish failures; restore server-disable flags; native-library bundling; unexpected sidecars; staged validation failure; rollback after promoted validation failure; successful staged probes/promotion; and exact-token forwarding for report and diagnostics. It changes only temporary checkouts and is wired into the Linux managed-host CI job.

Final focused checks also passed: six PowerShell/JSON configuration pairs, 75 affected-selection assertions, 16 UI contract checks, the current 162-test managed suite, and root apphost `--help`/`--version`. The documentation build completed with benchmark scanning skipped and coverage skipped. With no retained benchmark runs, the generator emitted an empty overview; DocFX reported 37 metadata warnings (35 duplicate parameters and 2 duplicate source files) and 2 invalid-link warnings during site generation. A no-scan empty-array bug in the benchmark page generator was fixed and recorded in the canonical changelog.

Bootstrap restores and builds `src/devops/Rowles.LeanCorpus.DevOps.Tests/Rowles.LeanCorpus.DevOps.Tests.csproj` in locked mode, then separately verifies the host's multi-RID restore. No dedicated DevOps solution is required; the test project's references provide the isolated build graph. The managed test apphost is run directly by setup because the legacy suite registry remains on PowerShell. A new hosted CI matrix runs setup on Linux and Windows and probes direct plus dispatcher-forwarded builds; that workflow has not been run from this branch.

The Phase 2 report service is implemented and routed. The all-groups run `19246c98-5c1d-4999-9a8e-4fbf6563ccd9` succeeded on Linux: all 50 child processes exited zero, all 37 solution projects were evaluated, and the versioned document contains Git, Files and Code sections with rename and syntax-analysis warnings. The promoted single-file host ran through `devops.ps1 report git -Json -Top 3` as run `2bdf88d7-085d-4319-9db8-d0187cbf0622`, with 8/8 child exits. A first run killed during the local sandbox restore was reconciled as `interrupted` (`714dd4f4-c07a-42ac-8880-6933ff746c9e`); it is not counted as passing evidence. The legacy report missed line changes on similarity-detected rename records because it parsed `old => new` as a literal path. Managed NUL-safe parsing retains those changes, so source churn is higher by 7,196 additions and 4,247 deletions on this baseline; the contract fixture records this intentional correction. Equal-ranked rows now have stable ordinal ordering. Windows/macOS and hosted parity gates remain open.

## Build, clean and server contracts

The exact captured help is retained in `src/devops/Rowles.LeanCorpus.DevOps.Tests/Fixtures/Legacy/legacy-help.txt` with exit code `0`. Machine-readable command cases are in `src/devops/Rowles.LeanCorpus.DevOps.Tests/Fixtures/Contracts/legacy-command-contracts.json`.

| Command | Defaults and effective behavior to preserve |
|---|---|
| `build` | Release. A solution build uses `Rowles.LeanCorpus.slnx` and leaves each project on its declared target frameworks. A project build defaults to `net11.0`. Effective flags include `--disable-build-servers -m:1 -p:UseSharedCompilation=false --tl:off`, a per-run binlog and text log under `artifacts/build/runs`, and terminal run state. |
| `clean` | Defaults to `default`; owned targets include `build`, `test`, `coverage`, `docs`, `diagnostics`, `benchmark`, `package`, `cache`, and `all`. Default preserves package output and benchmark data/runs. `all` owns the whole artefact root and is tested only on disposable fixtures. |
| `server start` | Defaults to `net11.0` and `Debug`; foreground `dotnet run` for `src/server/Rowles.LeanCorpus.Server.Local`. Loopback is `127.0.0.1:5080`; `-External` binds `0.0.0.0:5080` with `--AllowedHosts=*`. Preserve `-NoBuild`, `-NoRestore`, and individual application passthrough tokens. |

The current Linux managed suite passes 162/162. Build keeps each solution project's declared TFM, defaults explicit project builds to `net11.0`, records child output and terminal run state, and has passed through both direct and PowerShell-forwarded routes. Clean has only run destructive operations in disposable fixtures and covers root containment, no-follow deletion, active-run protection, bootstrap/apphost protection and the full target map. The foreground server ran on loopback at `127.0.0.1:5080`; `/` returned 404, then Ctrl+C produced exit 130, a cancelled manifest and retained stdout/stderr logs.

The SDK 11/MSBuild API probe raised a `System.Runtime, Version=11.0.0.0` load error while resolving SDK workload components in the net10 host. The narrow `dotnet msbuild -getProperty:Configuration,TargetFrameworks,TargetFramework,TargetPath` fallback passed and retains diagnostics. This records the fallback result; the API probe itself did not pass.

## Managed diagnostics

All seven standalone diagnostics variants route through the managed apphost, and `devops.ps1 diagnostics ...` preserves exact argument tokens when forwarding. The five tools are pinned in `.config/dotnet-tools.json` at version `9.0.661903`. The managed suite passed 161/161, and the bootstrap forwarding smoke passed 39 checks.

Linux live attach run `bf654452-2a53-422b-8fd6-9341a5574b34` completed a one-second counters and trace capture against a temporary .NET process. Both tools exited zero; the run contains `counters.json` (3,829 bytes), `trace.nettrace` (750,154 bytes), child logs, SDK/Git provenance logs and a succeeded manifest. It used Debian 13 x64 and SDK `11.0.100-preview.7.26381.103`, with source commit `5770528ebe240deaa50c9bf6e76aca8043605404` and a dirty worktree. A sandboxed attach attempt (`6aa630a1-21aa-4b80-b97d-0453ad0d5bce`) failed because diagnostic IPC was unavailable inside the sandbox; it is retained as failed evidence and not counted as a product pass. Windows, macOS and hosted diagnostics parity remain open.

## Configuration and retained data

The six PowerShell configuration files are `benchmark-datasets.psd1`, `benchmark-groups.psd1`, `benchmark-strategies.psd1`, `benchmark-suites.psd1`, `code-areas.psd1`, and `test-suites.psd1`. Convert by owning phase, retain `.psd1` as the authoritative source while legacy readers remain, and validate semantic equivalence before switching a managed reader. Package policy, test config, frozen migration fixtures and DataForge boundaries are separately inventoried in the JSON record.

## ADR and evidence policy

ADR context for this milestone: the docs workflow generates `artifacts/docs/content/articles/ADRs/index.md` from `docs/content/articles/ADRs/`; the task-selected record is `docs/content/articles/ADRs/ADR030-unified-devops-artefact-and-test-evidence.md`. Keep ADR030’s unique run identity, source/SDK/framework/OS provenance, terminal outcome and artifact references in managed manifests. Additional ADRs are not read unless the task specification adds them or implementation reveals a direct uncovered conflict.

## Baseline commands

- `./devops --help` and `./devops --version`: passed after both Linux promotions; help and version remain independent of repository discovery.
- `./devops build`: baseline-only run completed successfully from the recorded revision; 974 warnings and 0 errors. The run manifest and logs are in `artifacts/build/runs/20261021-072118-578e5e4b-linux/`. Warnings include unavailable NuGet vulnerability feeds and existing analyzer warnings.
- `./devops build --project src/devops/Rowles.LeanCorpus.DevOps.ProcessTestChild/Rowles.LeanCorpus.DevOps.ProcessTestChild.csproj --framework net10.0`: passed through both the root apphost and `pwsh ./devops.ps1` forwarding route.
- `./devops server start`: loopback server started on `127.0.0.1:5080`; `/` returned HTTP 404, confirming the listener. Ctrl+C stopped it with exit 130; the run manifest records cancellation and both child logs.
- Historical Windows pre-provisioning probe: the guest checkout had no `.git` marker and Git was absent, so that probe did not run setup. A later isolated full-history bundle at exact clean candidate `a37cb13e` passed Windows setup; see the final candidate receipt below. The VM was reverted to its recorded snapshot after qualification.
- Windows hosted CI, macOS and non-`linux-x64` RID execution: not run; no qualification claim is made.
- A final confirmatory `pwsh ./setup.ps1` attempt after the earlier two successful Linux runs was interrupted with exit 130 during locked restore. It is incomplete and does not count as a pass or replace the earlier successful bootstrap evidence.
- `./devops test all -Framework net10.0`: not run.
- `./devops pack -NoBuild`: not run.

## Migration state rules

- `inventoried`: source, command or caller is recorded.
- `implemented`: code exists; parity is not implied.
- `parity-proven`: contract, failure, evidence and required-platform comparisons are retained.
- `routed`: dispatch uses the managed slice without legacy fallback.
- `retired`: all active callers are migrated and the old source is removed after the zero-caller check.

Generated JSON ledger: [`migration-ledger.json`](migration-ledger.json).

## Testing continuation

The shared managed planner now preserves the ordinary, unit, flaky, CI,
explicit-test, diagnostics and coverage request defaults before execution.
Invalid repetition, profile, duration and affected-source options fail before
child processes start. The dedicated DevOps test project uses the existing
GitHub Actions reporter at `2.4.0` to exercise the real CI argument contract;
its dependency remains inside the separate DevOps package graph.

The Linux managed setup passed 226/226 tests with zero build warnings/errors
and promoted the single-file host. Build smoke run:
`906072de-a8ae-4997-9f97-4de057994ee7`. Managed execution run
`dcebf4c5-9c8d-433a-8a61-e9498eb0fbe9` used CI-prepared output, resolved the
actual MTP executable through MSBuild, ran two filtered tests twice, parsed
both real TRX reports, and recorded a successful terminal manifest with two
complete attempts and two stable passing test identities.

This evidence covers the managed planner and executor on Linux. Public
`test` and `aot` routing, full Windows/Linux parity, isolated stress,
deliberate-skip qualification, managed Native AOT qualification and legacy
retirement remain open under specification section 18.

### Runner adapters and qualification

The executor now uses the required `MtpExecutableTestRunner` and
`NativeAotTestRunner` adapters. Shared process lifecycle handling retains
arguments, scoped environment, output logs, timeouts and cancellation. Native
process failure, timeout and cancellation tests use a regular process fixture;
they do not claim Native AOT publication evidence.

Setup passed with 230 tests succeeded, 2 intentional explicit-test skips and
zero failures, plus zero build warnings/errors. Build smoke run:
`35ac31dc-e1d4-408c-8bfd-c5d3a972b8ce`. The real managed MTP run
`95852349-8646-4d31-8041-12b81c29aa6d` executed twice with two passing tests and
two intentionally excluded Native AOT qualification cases per iteration.
Aggregation retained two `AlwaysPasses` and two `Skipped` identities. MTP's
TRX contains no reason for these explicit-filter exclusions; the raw stdout
retains the reasons, and structured reason normalisation remains open.

Both explicit managed Native AOT qualification cases passed on Linux x64:
.NET 10 run `97d2a050-14d6-4177-8bad-254a0b9acedb` and .NET 11 run
`7a2c4fd2-1af5-4a4f-932b-a99c12ffc31e`. Each published the actual smoke project
through managed preparation, executed its native binary through the native
adapter, passed 122 tests and recorded exit zero and a successful manifest
without requiring TRX. The evidence records source
`678e4feddcd4c40c178eb9f686009868f8dbd34e` with a dirty worktree containing this
implementation. These are local qualification results, with no hosted or
Windows qualification claim.

Remaining testing work includes the public command and backup/restore matrix,
real managed Core MTP telemetry capture and full Markdown/JSON report compatibility, SDK provenance in
test manifests, structured skip reasons, isolated stress, platform parity and
legacy retirement. The full migration remains in progress.


### Canonical run envelope and existing UI compatibility

New managed run envelopes use ADR030's `run.json`. The shared store continues
to read historical `manifest.json` files, prefers canonical evidence when both
exist, and writes subsequent updates to the canonical path without changing
the historical file. Corrupt canonical evidence fails explicitly instead of
falling back to stale history. Package and dataset manifests retain their
separate filenames and contracts.

The existing runs UI discovers both formats and projects managed provenance,
terminal state and test attempts into its display model. Legacy report shapes
remain supported. The projection preserves incomplete and unscheduled counts
and per-attempt failures without changing raw evidence. Cleaning discovers
both envelope names and protects active runs across all eleven supported
categories using the shared category list.

The UI contract smoke passed 23 checks, including mixed discovery, canonical
precedence, corrupt canonical evidence, provenance, incomplete targets and
legacy report handling. A read-only inspection of retained runs also found
the real managed MTP and both Native AOT qualification runs. The latest setup
passed 233 tests, skipped 2 intentional explicit cases and failed none, with
zero build warnings/errors and successful publication/promotion. Build smoke
run: `581cf1df-43f9-4f66-aac2-def4ed3967b6`. Evidence records parent source
`a11da52cdc3b0c6e482accb9f5c3ef4573eeec5b` with this implementation in the dirty
worktree. Other report consumers, full report-output parity, structured skip
reasons, platform gates and final cutover remain open.

### SDK selection in test run evidence

`TestExecutor` now resolves the SDK through the shared `DotNetSdkResolver`
before preparation. A successful `run.json` records
`dotnet-sdk-requested`, `dotnet-sdk-selected` and `dotnet-sdk-roll-forward`;
an invalid or unavailable SDK policy terminates the run before target
preparation. The focused test `DoesNotPrepareTestsWhenSdkPolicyCannotBeResolved`
checks the failure path. The real planner-to-MTP integration run
`88ab4c73-ac73-43fd-994d-2da30bf073f9` executed two prepared apphost attempts
on Debian 13 x64 and recorded requested/selected SDK
`11.0.100-preview.7.26381.103` with `latestPatch`. The run identifies source
`a11da52cdc3b0c6e482accb9f5c3ef4573eeec5b` with a dirty worktree. The SDK is
resolved before preparation on Linux. The updated managed Native AOT runs
`f34cdfcb-f2ab-48b1-941b-c52ca4b6f45d` (`net10.0`) and
`1fdf9cdc-a2da-4950-93de-250322126662` (`net11.0`) each passed 122/122 tests
and recorded the same SDK policy in their manifests. Windows, macOS, hosted
parity and remaining report consumers remain open.

### Structured skip reasons from MTP output

`TestResultReader` now supplements a missing TRX skip reason only when the
captured MTP stdout contains the matching test name and its adjacent indented
reason line. Existing TRX reasons take precedence. Missing, oversized,
unreadable or ambiguous console evidence leaves the reason unset; the original
stdout remains retained.

The real planner-to-MTP run `e678077a-3b5a-4d3e-9c22-176a4eb0835c` passed two
prepared apphost attempts on Debian 13 x64. It normalized four explicit-filter
skip reasons that TRX omitted and recorded the selected SDK policy. The
DevOps setup passed 234/236 tests, with the two existing intentional AOT
qualification skips, built with zero warnings/errors, and promoted the Linux
host; build smoke run `ac310c6e-d7f2-43d3-ac9e-90e1b9c327bc`. The source commit
was `158f8068a1d56e78c34e886ca2bf19a4dadea0bb` with these changes in a dirty
worktree. Windows console-format parity, report consumers, public test/AOT
routing, stress and broader platform qualification remain open.

### Managed Markdown and timing reports

The managed executor now writes `report.md` and `timings.csv` alongside the
existing `report.json`. The Markdown summary includes environment and target
details, preparation timings, target outcomes, test classifications, failure
details and unscheduled targets. The CSV keeps the legacy column order and
quotes every field, including embedded quotes and commas.

The real planner-to-MTP run
`e38363a1-2fce-43f1-8492-7f7ad2840474` completed two prepared executions and
produced all three report files. It had no telemetry files, which the report
represents as zero executions and an explicit absence message. Its source was
commit `209fe731b36a016a534dd739a852f8b57306fbbc` with the telemetry projection
changes in the dirty worktree. The latest setup passed 234 tests, skipped three
intentional explicit qualification cases, built with zero warnings or errors,
published and promoted the Linux host, and passed staged/promoted help and
version probes. Build smoke run:
`ec764552-7c17-4470-aa50-10235b8d5416`.

The managed report regression fixture also verified summing two telemetry
summaries, retaining both NDJSON test records, exposing stream counters in
`report.json` and rendering the aggregate in Markdown. A separate explicit
managed Core MTP qualification, run `cc9fc88d-a479-44ac-bb2c-905bf44c250e`,
built `core/net10.0`, ran
`TelemetryExecutionStoreTests.SummaryModeDoesNotPersistCleanPassingTest`, and
passed 1/1. Its report captured one telemetry execution, one started and
finished test, one passed test, zero telemetry errors, no truncation, and the
real summary at
`targets/core-net10.0-3e99b8f0/001/telemetry/summary.json`. The run used source
`209fe731b36a016a534dd739a852f8b57306fbbc` with the current changes in the
dirty worktree.

This verifies basic managed Core telemetry capture and reporting on Linux.
Full legacy Markdown/JSON field parity, other report consumers, Windows/Linux
parity, isolated stress and public `test`/`aot` routing remain open.

### Post-pull bootstrap preflight

At issue-97 base `4244928c4539e986dedaa974b6045b27f8435be3`, the bootstrap
preflight was extended to reject missing managed projects and each required
`.config/devops` JSON file before restore, build or publish. The first real
setup attempt exposed that root `Directory.Build.targets` is optional in this
checkout; the smoke fixture was corrected to omit it and setup now checks only
inputs the project graph requires. The regression smoke passed 77/77 checks.

The real Linux setup passed with SDK `11.0.100-preview.7.26381.103` on Debian
13 x64: locked restores, managed test-project graph build (0 warnings, 0
errors), 235/238 managed tests passed with three explicit qualification tests
skipped, single-file host publication, staged help/version/build probes,
promotion and promoted help/version probes. Managed build smoke run
`e65171ec-1573-4934-b106-4d980a626df4` succeeded. The promoted executable and
manifest hashes are recorded in `latestBootstrapPreflight` in the adjacent
JSON ledger. At that time, this run used a dirty worktree based on `4244928c4`;
its final-SHA, Windows and hosted gates were still open. The sandboxed restore
attempt exited 130 before project restore and remains incomplete, not passing
evidence. The later clean-candidate qualification is recorded below.

### Clean code candidate bootstrap qualification (2026-10-22)

Code candidate `a37cb13e0a79f33a2550fdb22a8950d1a77b79db` was clean on `issue-97` and equal to `origin/issue-97` when qualified. Linux setup passed locked restore, a managed build with 0 warnings/errors, 235/238 tests with three explicit filter skips and zero failures, single-file publication, staged/promoted help/version probes and project-build smoke run `7bb8c9ee-2c81-4ccb-9c0c-4075c24fffda` (terminal succeeded). Linux promoted executable SHA-256: `ed24764d0f7fc02d04a64a27e75ec8a417152667c36dd550ddd0392290eb100e`; manifest SHA-256: `67b0654b91d075e2512849b06a35bebd243f4f511e3a17bf4cdf98d8fe378276`. Version reports `1.0.0+a37cb13e0a79f33a2550fdb22a8950d1a77b79db`. Linux bootstrap failure fixture passed 77 checks.

The exact same clean source passed Windows Server 2025 Standard Evaluation build 26100 x64 setup using PowerShell 7.6.5 and SDK `11.0.100-preview.7.26381.103`. Terminal exit was 0; 235/238 managed tests passed, three explicit filter cases were skipped, and the build had zero warnings/errors. Build smoke run `915169e3-cf44-4b7f-988a-c0bbbdb2d96e`; promoted host SHA-256 `66641dead997a03ad1dc5584d9e84ce27163281a11521ef5ee61234aeee0652e` matched manifest SHA-256 `e2b3d2adca89a6cf95f7b3b017fddadb008ea132929db9b4c8b5285294709550`. The outcome receipt and logs are under the workspace path `.notes/Issues/#8 Rowles.LeanCorpus.DevOps/evidence/windows-20261022/a37cb13e/`. The disposable VM returned to snapshot `issue-97-qualification-20261022`.

At `a37cb13e`, the docs build passed with 37 existing DocFX metadata warnings and no site warnings; static validation covered 217 Markdown pages, 4 TOCs, 1,745 HTML pages, 23,853 local references and 4,098 fragments, and the browser suite passed 11/11. The `docs/` source tree has 276 tracked paths / 180 Markdown files; its two benchmark-named tracked paths are authored guides. The historical 445 / 274 count includes 146 generated benchmark paths / 76 Markdown pages.

At the `a37cb13e` candidate, Hosted Build remained unrun. `build.yml` supports `workflow_dispatch`, but `issue-97` is not a push-triggered branch and no PR-triggered run was found for that SHA. GitHub CLI authentication was unavailable at the time. macOS/other RID parity and later #8 phase gates also remained open; this setup evidence did not close them. That evidence-only ledger update had no separate release changelog entry.

### Hosted Build and managed matrix continuation (2026-10-22)

Manual Build run `39775959860` was dispatched against clean issue-97 commit
`228812dfea363e9ef41c5c2ed12864d840a711f6` and is terminal with overall
failure. Linux and Windows managed DevOps foundation, Linux/Windows/macOS
platform smoke, Linux net10/net11 tests, Windows tests, package validation and
package consumer jobs passed. Coverage failed only on
`PhysicalIndexRuntimeResourceTests.CloseDrainsARealAdmittedWriteAtItsFlushSubmissionBarrier`,
which timed out at its 10-second flush submission barrier; 4,290 Core tests
passed, one failed and one skipped, and all 63 SourceGen tests passed. The
dependent docs-site job was skipped. This failure is in Core test code outside
the managed DevOps change boundary. [Hosted run](https://github.com/jordansrowles/LeanCorpus/actions/runs/39775959860).

The final-source explicit qualification passed all five legacy variants on
Debian 13 x64, framework net10.0, with one launch and two FsCheck iterations.
Parent run `80a6cb0a-ff62-4d66-827b-88cc4213e13d` retained source manifest
fingerprint `f0b1749a060ab7aa098c0acac8d4b160f9ae2544371ba369c1921f274b3c971c`,
five measurement reports and five linked child runs. The source manifest reader
passed a Git inventory probe with a tab and newline in a filename. Setup run
`92eb9c68-b881-444c-96f2-80aef2e9505b` passed a zero-warning/error build and
248/254 managed tests; six tests were intentional explicit skips. The managed
matrix is not a public command route. Windows matrix parity, normal and stress
counts, the complete §18 gates and later migration phases remain open. The
hosted run above predates these dirty local matrix changes.

### Managed interactive workbench candidate (2026-10-22)

On the dirty `issue-97` source tree based on pushed commit
`3718a8c5704ff020b11ebcc68ba2dff658916f4c`, `pwsh -NoProfile -File
./setup.ps1` completed on Debian 13 x64 with SDK
`11.0.100-preview.7.26381.103`. The managed project graph built with zero
warnings/errors; all 291 DevOps tests completed, with 283 passed, 8 explicitly
skipped and zero failed. The `linux-x64` host was published, promoted and
passed build smoke `fc453048-f54a-4f32-b32d-38f076e40b8a`. The managed
bootstrap dispatcher smoke passed 81 checks. `./devops ui` under redirected
I/O returned the documented exit code 2 and explanation.

The workbench currently provides managed test selection/execution, benchmark
suite/strategy/framework/area/group selection, DataForge operations and recent
run observation. Its benchmark selection test confirms configured
`Serialisation` area/group labels pass unchanged. Linux local implementation
evidence does not close the specified terminal-resize/focus/cancellation and
historical-route comparisons, Windows/macOS parity, hosted validation or later
Phase 8 retirement gates.

After updating the source-controlled migration ledger, docs build `dee19494-e5fd-4fc3-8de7-c3e8cc00dd66` passed. DocFX metadata
reported 37 existing warnings and the site build reported zero warnings.
Docs acceptance run `a1109831-4ffe-4216-8667-722e21ae007a` passed on net10.0;
static checks covered 217 Markdown pages, 4 TOCs, 858 HTML pages, 20,267 local
references and 4,078 fragments. Its linked docs-site test run
`e3a4c0c9-0515-40df-8586-bd3d8fd6386c` passed 11/11. Initial sandbox attempts
were excluded: locked restore/DocFX worker IPC and browser local-bind were
denied by the restricted environment; both workflows passed with the required
access enabled. The workbench diff and docs content were still uncommitted at
this evidence point; hosted exact-commit qualification remains open.

### Current-SHA hosted Build failure analysis and repair (2026-10-22)

Manual Build run `39813635823` checked out exact source
`68815b95cbeedd5a2ad0bed294eeb11bf7da03ec` and is now terminal with overall
failure. Linux managed foundation, Linux net10/net11 tests, Linux x64/ARM64,
Windows x64 and macOS ARM64 platform smoke, package validation and package
consumer passed. Both Windows bootstrap jobs failed as described below.
Coverage failed because the job invoked `./devops.ps1 coverage` without first
running `setup.ps1`; the dispatcher reported that the managed `./devops`
apphost was missing. The dependent docs-site job was skipped.

Both Windows managed-foundation and Windows-test bootstrap runs failed on the
same managed DevOps test:
`RepositoryDocumentationImporterTests.GeneratesAdrGraphIndexAndStagedRelationshipLinksWithoutEditingSources`.
The test generated the relationship section using `Environment.NewLine`, then
counted it with `(?m)^## Relationships$`. On Windows the generated CRLF leaves
the carriage return before the line terminator, so the assertion found no
match. This was a test assertion portability defect; the generator's repeated
write removes existing CRLF sections with its CRLF-aware pattern. The Windows
setup summary was 273 passed, 1 failed, 8 intentionally filtered skips. The
Windows full product build and test suite did not start.

The assertion now accepts an optional carriage return. The focused test passed
1/1 on Debian 13 x64 after a successful DevOps project build (build run
`ef8b577a-b900-4546-a2da-420b262e3468`). The coverage job now bootstraps the
managed host and invokes `./devops coverage` directly; hosted verification is
still required. In the Windows-test job, the follow-up build-diagnostics upload
also failed because `artifacts/build/runs` did not exist before setup
completed; that step now warns when that optional build-run path is absent.
The separate `windows-test-results` artifact did upload: SHA-256
`f323a2615f24636b10573e7abb3c09fdff86c6102ecacdba0e71e5183d202be9`.

The repairs are local and uncommitted at this ledger revision. Commit and push
them to `issue-97`, then dispatch a new Build against that exact commit. Run
`39813635823` remains pre-repair evidence; do not use it to qualify the fixes.

### Direct managed CI workflow hand-offs (2026-10-23)

Commit `6c610fd57493e8a9abb7d3032c19949fc6c6c397` on `issue-97` changes the
product-build steps in Build, stress and diagnostics workflows to select the
checked-out `./devops` or `./devops.exe` after setup. It also changes the
manual benchmark workflow to invoke that apphost directly. The artefact
infrastructure smoke's workflow-contract checks failed against the previous
workflow content and passed after the change on Debian 13 x64 with PowerShell
7.5.4. All four changed workflow files parsed with PyYAML, and the source
ledger JSON and `git diff --check` passed.

Hosted Benchmark run [39879445230](https://github.com/jordansrowles/LeanCorpus/actions/runs/39879445230)
completed successfully at the exact source SHA above on `ubuntu-latest`, with
suite `core`, strategy `fast`, framework `net10.0` and `dry_run=true`. Setup
completed for `linux-x64`; 285 managed DevOps tests passed, 8 explicit filters
were skipped and none failed. The `Run benchmarks` step passed and logged
`Benchmark dry run completed; no benchmark targets were executed.` The
`core` selector resolved to the Core benchmark project with its internal
`--suite all` selection, as the managed planner specifies. This is launcher,
setup and argument-path evidence only; no benchmark measurements were made.
The subsequent Build run at this SHA and the Windows/macOS stress/diagnostics
workflows remain outstanding. No additional changelog entry was added for
this evidence-only ledger update.
