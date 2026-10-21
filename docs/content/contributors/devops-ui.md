# Interactive DevOps workbench

The DevOps workbench is an interactive front end over the existing `./devops` commands. It is optional and does not load PwshSpectreConsole during builds, tests, CI or other normal DevOps commands.

## Install and start

The supported PowerShell runtime is 7.4 or newer. Install the pinned UI module once for the current user, then launch the workbench in an interactive terminal:

```powershell
./devops setup -InstallUi
./devops ui
```

`./devops setup` reports whether the UI dependency is installed but does not install or import it. `./devops ui --help` reports the runtime and pinned module version. The normal `./devops` commands remain available when the module is absent or the terminal cannot support the UI.

## Workflows

The home page groups the normal DevOps surface into Run and Validate, Observe, Data, Build and Distribution, Server, and Maintenance.

Every page identifies itself as **LeanCorpus DevOps**. The separate index browser identifies itself as **LeanCorpus CLI Workbench**. Command review uses a bordered panel; execution and destructive confirmations default to **no**.

Test repetition is selected before the run count. Choose the flaky preset to start with 30 repetitions, then enter the count you need. The dedicated **Test options** screen selects Diagnostics, CollectCoverage, FailFast, CI, FailWarnings and NoRestore together. Press Space to toggle options and Enter to continue; all are initially clear. Explicit-test policy is selected separately.

Timeouts accept units such as `120s` or `2m`, or `off`. Bare numbers in the UI mean seconds: entering `120` produces `-HangTimeout 120s` in the command review.

- Test runs select multiple registered suites, areas, categories and techniques. Values come from the test registry and metadata enum. Affected tests show the changed-file and target selection returned by the normal affected-test resolver.
- Test execution displays target lifecycle events and TRX-derived counts. The event stream is versioned NDJSON written only when the UI starts a test run; it does not derive status from formatted console text.
- Benchmark discovery uses the benchmark suite, strategy and group registries. Users can narrow named suites before multi-selection or choose registered area/group combinations.
- Runs show retained test, coverage, diagnostics and benchmark manifests, test reports, timings, failures and an artefact tree.
- Diagnostics choose from detected .NET processes and dispatch the selected existing diagnostics command.
- DataForge profiles are read from `./devops dataforge profiles -Json`. Generation, inspection, verification, reproduction and reference operations still use normal DataForge orchestration.
- Build, pack, documentation, benchmark publication, server, setup, clean, repository health and benchmark-data workflows dispatch their matching `./devops` command.

Each executable workflow prints its normal command before it runs. Clean, overwrite, package cleanup and remote benchmark actions require explicit confirmation.

## Test event contract

When `LEANCORPUS_DEVOPS_UI_EVENTS_FILE` is set by the UI, `./devops test` appends schema version 1 NDJSON events such as `TargetsResolved`, `RunStarted`, `TargetsPrepared`, `TargetDiscovered`, `TargetStarted`, `TargetProgress`, `TargetCompleted`, `RunWarning`, `RunError` and `RunCompleted`. Event writing is observational. A failure to write an event does not change the test result.

See [DevOps entry point](devops-entry-point.md) for the regular command contract. The owning tooling and test projects are listed in `src/devops/CONTRIBUTING.md`.

The UI regression smoke uses the pinned Spectre module and an isolated child process to exercise command capture, live progress, bracketed output and failure exit codes:

This standalone check follows the existing DevOps validation-script convention. A future `./devops setup -ValidateUi` route would make it available directly from the entry point.

```powershell
pwsh -NoProfile -File scripts/devops/validation/ui-smoke.ps1
```
