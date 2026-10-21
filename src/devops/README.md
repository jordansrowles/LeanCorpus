# DevOps projects and repository tooling

Use this directory when you need to run or extend LeanCorpus tests, benchmarks, coverage, profiling or command-line tools. The supported repository interface is `./devops` on Linux and macOS and `./devops.ps1` on Windows.

## Choose a task

| I want to... | Command |
| --- | --- |
| Build the normal Release target | `./devops build` |
| Run every configured test route | `pwsh ./devops.ps1 test` |
| Run tests affected by current changes | `pwsh ./devops.ps1 test -Suite affected` |
| Run one production area | `pwsh ./devops.ps1 test -Suite core -Area Index` |
| Repeat a focused target with artefacts | `pwsh ./devops.ps1 test core --count 3 --filter 'FullyQualifiedName~Writer'` |
| Capture managed test diagnostics | `pwsh ./devops.ps1 test core --diagnostics` |
| Validate Native AOT | `pwsh ./devops.ps1 aot` |
| Attach standard .NET diagnostics | `./devops diagnostics ps` |
| Generate coverage and HTML | `pwsh ./devops.ps1 coverage -Clean -GenerateReport` |
| List benchmark suites | `pwsh ./devops.ps1 benchmark -List` |
| Build documentation | `pwsh ./devops.ps1 docs build` |

Run `./devops --help` for the complete current command surface.

> [!TIP]
> Start narrow while developing. Run affected selection before hand-off, then add broader validation only for the compatibility, platform or performance boundary you changed.

## Run focused tests

A test selection combines independent dimensions:

| Dimension | Question | Examples |
| --- | --- | --- |
| Suite | Which project should run? | `core`, `text`, `sourcegen`, `architecture`, `server-abstractions`, `server-core`, `server-integration`, `aot` |
| Area | Which production contract changed? | `Index`, `Search`, `Store`, `Filters` |
| Category | What role does the test have? | `Unit`, `Integration`, `Chaos` |
| Filter | Which runner-level name or expression matches? | `FullyQualifiedName~Writer` |

Examples:

```bash
pwsh ./devops.ps1 test -Suite core -Area Search
pwsh ./devops.ps1 test -Suite core -Area Index -Category Chaos
pwsh ./devops.ps1 test -Suite text -Area Filters
pwsh ./devops.ps1 test -Suite core -Filter 'FullyQualifiedName~Writer'
```

`Area` and `Category` become trait filters. `Filter` is passed to the test runner.

## Repeat tests and inspect artefacts

Use `--count` for sequential repetitions. Preparation happens once for each
distinct project and framework, while every repetition starts a fresh managed
or Native AOT process:

```bash
pwsh ./devops.ps1 test core --count 3 --filter 'FullyQualifiedName~Writer'
pwsh ./devops.ps1 test core --flaky --count 5
pwsh ./devops.ps1 test core --count 30 --fail-fast
```

Repeated, diagnostic and CI runs write one run directory under
`artifacts/test/runs/<run-id>/`. It contains selected targets, environment,
stdout and stderr, canonical MTP TRX results, checkpoint state and the
`report.md`, `report.json` and `timings.csv` reports. CI and flaky runs use
summary telemetry: one execution summary plus `tests.ndjson` entries only for
failed or anomalous tests. Clean passing tests have no telemetry attachment.
`--flaky` repeats 30 times by default, selects the stress parallel profile unless
`--profile` overrides it, and keeps summary evidence.

Only `--diagnostics` enables full managed telemetry and MTP diagnostic logging.
It writes shared activity, metric, runtime and test streams with a combined
128 MiB raw-stream limit per target execution. Each stream records written and
dropped records, bytes and truncation status; hitting a limit does not fail the
tests. `--diagnostics` also wins over `--ci` and `--flaky` for telemetry mode.
A failed test does not stop later repetitions unless `--fail-fast` is selected.

## Use the GitHub stress workflow

Open **Actions > Test Stress > Run workflow** for deliberate repeated runs.
It always uses the stress parallel profile and summary telemetry. Open
**Actions > Test Diagnostics > Run workflow** when full managed telemetry and
MTP logs are needed. Linux and Windows can be selected independently in both
workflows. `count` controls how many fresh test processes are run, while
`CHAOS_ITERATIONS` controls property or chaos cases inside each process.

Useful configurations include:

```text
Quick stress:                  count=5, chaos_iterations=25
Repeated flaky hunt:           count=30, chaos_iterations=25
Chaos stress:                  count=5, chaos_iterations=1000
Windows Store diagnostics:    windows=true, linux=false, suite=core, area=Store
```

Both workflows accept suite, framework, area, category, filter, fail-fast,
hang-timeout and outer process-timeout inputs. Download `test-stress-linux` or
`test-stress-windows` for stress runs, or `test-diagnostics-linux` and
`test-diagnostics-windows` for full telemetry. Reports are under
`artifacts/test/runs`.

For CI jobs whose managed output has already been built, use `--ci`. It skips
managed restore and build, but still publishes Native AOT targets when they
are selected:

```bash
pwsh ./devops.ps1 test all --ci --framework net10.0
```

## Standalone diagnostics

Use `--diagnostics` to enable MTP diagnostic logging for a test process. For
an explicitly selected .NET process, use the standard diagnostic tools:

```bash
./devops diagnostics ps
./devops diagnostics counters --pid 1234
./devops diagnostics trace --pid 1234
./devops diagnostics gcdump --pid 1234
./devops diagnostics dump --pid 1234 --type Mini
./devops diagnostics symbols path/to/core.dmp
./devops diagnostics capture --pid 1234 --duration 5s
```

On Windows, run the managed apphost as `./devops.exe diagnostics ...` after
`pwsh ./setup.ps1` publishes it. `pwsh ./devops.ps1 diagnostics ...` forwards
the same arguments through the checked apphost.

Trace, GC dump, dump and capture output is written under
`artifacts/diagnostics/runs/<run-id>/`. Dumps can contain sensitive application
memory. Pass tool-specific options after `--` where the command supports it.

## Run affected tests

```bash
pwsh ./devops.ps1 test -Suite affected
```

Affected selection:

1. collects changed, staged and untracked paths;
2. maps production paths through `scripts/devops/config/code-areas.psd1`;
3. builds the required `suite:area` targets;
4. runs tests carrying matching `Area` traits.

> [!IMPORTANT]
> A new production path must have an affected-test mapping. The runner should not silently treat an unmapped source area as requiring no tests.

## Validate Native AOT

```bash
pwsh ./devops.ps1 aot
```

The AOT route publishes and runs a smoke executable for both supported frameworks. It is not a normal VSTest project.

## Generate coverage

```bash
pwsh ./devops.ps1 coverage -Clean -GenerateReport
```

Each invocation writes raw data and an optional HTML report under
`artifacts/coverage/runs/<run-id>/`.

Coverage proves that code executed. It does not prove that the assertions or oracle were useful.

## Run benchmarks

List suites and strategy presets:

```bash
pwsh ./devops.ps1 benchmark -List
```

Run a bounded smoke workload:

```bash
pwsh ./devops.ps1 benchmark -Suite query -Strat fast
```

Run the controlled preset:

```bash
pwsh ./devops.ps1 benchmark -Suite query -Controlled
```

Record corpus, workload, commit, framework, host state and provenance before making a comparison claim.

## Build documentation

```bash
pwsh ./devops.ps1 docs build
```

Skip expensive generated inputs when they are outside the documentation change:

```bash
pwsh ./devops.ps1 docs build -SkipBenchmarks -SkipCoverage
```

Serve the site locally with `pwsh ./devops.ps1 docs serve`.

The docs command reads `docs/site-config/imports.json` and stages selected
repository READMEs and contribution guides under
`artifacts/docs/content/generated/` before DocFX runs. Edit the canonical
repository file, not the generated site copy. API metadata, changelogs,
benchmarks, coverage pages and the site are also written beneath
`artifacts/docs/`.

## Project map

| Project | Purpose |
| --- | --- |
| `Rowles.LeanCorpus.Tests.Core` | Main LeanCorpus unit, integration and chaos tests |
| `Rowles.Text.Tests` | Standalone analysis correctness |
| `Rowles.LeanCorpus.Tests.SourceGen` | Source-generator output and diagnostics |
| `Rowles.LeanCorpus.Tests.Architecture` | Package and dependency boundaries |
| `Rowles.LeanCorpus.Tests.AOTSmoke` | Native AOT smoke executable |
| `Rowles.LeanCorpus.Tests.Shared` | Framework-agnostic fixtures and infrastructure |
| `Rowles.LeanCorpus.Benchmarks` | Core BenchmarkDotNet workloads |
| `Rowles.LeanCorpus.Benchmarks.Compression` | Compression workloads |
| `Rowles.Text.Benchmarks` | Text-analysis workloads |
| `Rowles.LeanCorpus.Profiling` | Profiling entry points |
| `Rowles.LeanCorpus.Cli` | User-facing maintenance CLI |

`Tests.Shared` is infrastructure, not a runnable suite.

> [!WARNING]
> Do not edit generated content under `artifacts/` manually. Change its source,
> configuration or generator.

To add tests or tooling, continue with [CONTRIBUTING.md](CONTRIBUTING.md).
