# DevOps projects and repository tooling

Use this directory when you need to run or extend LeanCorpus tests, benchmarks, coverage, profiling or command-line tools. The managed apphost is `./devops` on Linux and macOS and `./devops.exe` on Windows. CI invokes the managed host directly after using the root `devops.ps1 bootstrap` command. That root script is the maintained PowerShell entry point: `bootstrap` explicitly restores, tests and promotes the host; all other arguments go to the fingerprint-validated managed host.

## Choose a task

| I want to... | Command |
| --- | --- |
| Build the normal Release target | `./devops build` |
| Run the managed test executor | `./devops test all` |
| Run managed tests affected by current changes | `./devops test affected` |
| Run one managed production area | `./devops test core --area Index` |
| Repeat a focused managed target with artefacts | `./devops test core --count 3 --filter 'FullyQualifiedName~Writer'` |
| Capture managed test diagnostics | `./devops test core --diagnostics` |
| Validate Native AOT through the managed executor | `./devops aot` |
| Attach standard .NET diagnostics | `./devops diagnostics ps` |
| Generate coverage and HTML | `./devops coverage -Clean -GenerateReport` |
| List benchmark suites | `./devops benchmark --list` |
| Build a release archive | `./devops distribute --rid linux-x64` |
| Build documentation | `./devops docs build` |

Run `./devops --help` for the complete current command surface.

The canonical project inventory is `src/Rowles.LeanCorpus.slnx`, with focused
build filters documented in [`src/solutions/README.md`](../solutions/README.md).
Run `./devops config validate --solution-inventory` to detect drift between
tracked projects, the master solution and those filters.

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

The Core JSON metadata source paths, namespaces, test selectors, affected-source selectors and benchmark area/group selectors use British `Serialisation`. The interactive workbench also needs an ANSI-capable terminal; use the documented commands directly when ANSI selection is unavailable.

Examples:

```bash
./devops test core --area Search
./devops test core --area Index --category Chaos
./devops test text --area Filters
./devops test core --filter 'FullyQualifiedName~Writer'
```

The PowerShell compatibility shim forwards command arguments to the checked
managed host. Cross-platform release qualification remains separate from this
routing cut-over.

`Area` and `Category` become trait filters. `Filter` is passed to the test runner.

## Repeat tests and inspect artefacts

Use `--count` for sequential repetitions. Preparation happens once for each
distinct project and framework, while every repetition starts a fresh managed
or Native AOT process:

```bash
./devops test core --count 3 --filter 'FullyQualifiedName~Writer'
./devops test core --flaky --count 5
./devops test core --count 30 --fail-fast
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
./devops test all --ci --framework net10.0
```

Browser-required suites are explicit-only: `server-orchard-functional` and
`docs-site` do not run as part of `./devops test all`. `server-orchard` remains
in the generic selection. The Playwright package is pinned in
`Directory.Packages.props` (currently 1.63.0); build the selected test project,
then install and check its matching Chromium before running it:

```bash
./devops build --project src/server/devops/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests.csproj --framework net10.0
pwsh artifacts/bin/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests/release_net10.0/playwright.ps1 install --with-deps chromium
pwsh -NoProfile -File ./scripts/devops/check-playwright-runtime.ps1 -ProjectOutputDirectory artifacts/bin/Rowles.LeanCorpus.OrchardCore.Search.FunctionalTests/release_net10.0
./devops test server-orchard-functional --ci --framework net10.0
```

On Windows, omit `--with-deps`. Use the corresponding docs test output path for
`docs-site`, then run `./devops docs test --framework net10.0 --no-restore`.
The preflight fails if the built Playwright package version is inconsistent,
the expected Chromium executable is missing, or Chromium cannot launch. The
`test affected` selection keeps browser suites when their source areas match
and records `requiresBrowser` on the selected target so callers can route that
work to a browser-capable worker.

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
`pwsh -NoProfile -File ./devops.ps1 bootstrap` publishes it. Use `./devops.exe diagnostics ...` directly.

Trace, GC dump, dump and capture output is written under
`artifacts/diagnostics/runs/<run-id>/`. Dumps can contain sensitive application
memory. Pass tool-specific options after `--` where the command supports it.

## Run affected tests

```bash
./devops test affected
```

Affected selection:

1. collects changed, staged and untracked paths;
2. maps production paths through `.config/devops/code-areas.json`;
3. builds the required `suite:area` targets;
4. runs tests carrying matching `Area` traits.

If the selected target plan includes `requiresBrowser: true`, provision its
Playwright Chromium before running the affected tests. The command does not
download browsers implicitly.

> [!IMPORTANT]
> A new production path must have an affected-test mapping. The runner should not silently treat an unmapped source area as requiring no tests.

## Validate Native AOT

```bash
./devops aot
```

The AOT route publishes and runs a smoke executable for both supported frameworks. It is not a normal VSTest project.

## Generate coverage

```bash
./devops coverage -Clean -GenerateReport
```

Each invocation writes raw data and an optional HTML report under
`artifacts/coverage/runs/<run-id>/`.

Coverage proves that code executed. It does not prove that the assertions or oracle were useful.

## Run benchmarks

List suites and strategy presets:

```bash
./devops benchmark --list
```

Validate the managed DevOps catalogues without building products. Add `--deep`
to compare them with already-built Core, Text and Compression benchmark host
manifests; manifest checks do not run benchmark workloads.

```bash
./devops config validate
./devops config validate --deep
```

Run a bounded smoke workload:

```bash
./devops benchmark --suite query --strat fast
```

Run the controlled preset:

```bash
./devops benchmark --suite query --controlled
```

Select benchmark classes that match current dirty paths with
`./devops benchmark affected --dry`. The command refuses to run zero classes
and records changed paths and matched groups in the benchmark run evidence.
Area and group filters can narrow the selected classes.

Remote runs use SSH and tmux through the managed benchmark command:

```bash
./devops benchmark remote start --host jordan@debian --path /home/jordan/code/leancorpus --branch main -- --suite query --strat intense
./devops benchmark remote status --run-id <run-id>
./devops benchmark remote cancel --run-id <run-id>
```

The remote checkout must be clean and its managed apphost current. The local
run record keeps the remote host, path, branch, session and source revision.
SSH disconnects and cancellation leave remote lifetime unknown until a status
check confirms completion.

Record corpus, workload, commit, framework, host state and provenance before making a comparison claim.

## Build documentation

```bash
./devops docs build
```

Skip expensive generated inputs when they are outside the documentation change:

```bash
./devops docs build -SkipBenchmarks -SkipCoverage
```

Serve the site locally with `./devops docs serve`.

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
