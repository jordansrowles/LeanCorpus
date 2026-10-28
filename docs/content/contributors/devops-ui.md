# Managed DevOps workbench

The managed `ui` command provides an interactive Spectre.Console workbench over
the DevOps services. It calls the managed test planner/executor, benchmark and
DataForge handlers directly. It does not start the legacy public CLI to reuse
those operations.

## Start

After cloning or changing the managed DevOps source, build and promote the
current apphost:

```powershell
pwsh -NoProfile -File ./devops.ps1 bootstrap
./devops ui
```

On Windows, run `./devops.exe ui`. The compatibility shim also forwards
`pwsh ./devops.ps1 ui` to the fingerprint-validated managed host. No PowerShell
UI module is required.

The workbench requires an interactive terminal. When input or output is
redirected, it prints a short explanation and exits with code 2 without waiting
for input. Ctrl+C during a prompt exits with code 130.

## Workflows

- **Tests** selects one configured suite, framework, build configuration and
  execution profile, then confirms before calling `TestRunPlanner` and
  `TestExecutor`. Test run manifests, logs and result files use the normal
  managed test artefact paths.
- **Benchmarks** selects a configured suite, strategy, framework, area, group
  and either a Dry smoke or the full selected strategy. Areas and groups come
  from `.config/devops/benchmark-groups.json`; their configured names, including
  `Serialisation`, are passed unchanged to the benchmark service.
- **DataForge** previews or runs managed release qualification, or accepts a
  DataForge command as separate argument tokens. It shows the selection and
  asks before starting the operation.
- **Observe** lists up to 40 recent managed runs across the registered artefact
  categories. A selected run shows its state, source identity, inputs, run
  directory, failure summary and full artefact references.

The workbench uses ordinary terminal prompts rather than a fixed full-screen
layout. It recalculates each menu's page size from the current terminal width;
long operations continue to retain their normal full logs and run evidence.
For command-line, CI and redirected use, invoke the equivalent documented
`./devops` command directly.

See [DevOps entry point](devops-entry-point.md) for the managed command map and
argument contracts.
