# DevOps projects and repository tooling

Use this directory when you need to run or extend LeanCorpus tests, benchmarks, coverage, profiling or command-line tools. The supported repository interface is `./devops` on Linux and macOS and `./devops.ps1` on Windows.

## Choose a task

| I want to... | Command |
| --- | --- |
| Build the normal Release target | `./devops build` |
| Run every configured test route | `./devops test` |
| Run tests affected by current changes | `./devops test -Suite affected` |
| Run one production area | `./devops test -Suite core -Area Index` |
| Repeat a focused target with artefacts | `./devops test core --count 3 --filter 'FullyQualifiedName~Writer'` |
| Capture managed test diagnostics | `./devops test core --diagnostics` |
| Validate Native AOT | `./devops aot` |
| Generate coverage and HTML | `./devops coverage -Clean -GenerateReport` |
| List benchmark suites | `./devops benchmark -List` |
| Build documentation | `./devops docs build` |

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
./devops test -Suite core -Area Search
./devops test -Suite core -Area Index -Category Chaos
./devops test -Suite text -Area Filters
./devops test -Suite core -Filter 'FullyQualifiedName~Writer'
```

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

Repeated, flaky, diagnostic and CI runs write one run directory under
`artifacts/test/runs/<run-id>/`. It contains the selected targets, environment,
stdout and stderr, MTP TRX files where supported, checkpoint state and the
`summary.md`, `summary.json` and `timings.csv` reports. A failed test does not
stop later repetitions unless `--fail-fast` is selected. `--flaky` is a preset
for 30 repetitions unless `--count` supplies another value.

For CI jobs whose managed output has already been built, use `--ci`. It skips
managed restore and build, but still publishes Native AOT targets when they
are selected:

```bash
./devops test all --ci --framework net10.0
```

## Standalone diagnostics

The test runner uses MTP's diagnostic extensions for its own process. For an
explicitly selected .NET process, use the standard diagnostic tools:

```bash
./devops diagnostics ps
./devops diagnostics counters --pid 1234
./devops diagnostics trace --pid 1234
./devops diagnostics gcdump --pid 1234
./devops diagnostics dump --pid 1234 --type Mini
./devops diagnostics symbols path/to/core.dmp
./devops diagnostics capture --pid 1234 --duration 5s
```

Trace, GC dump, dump and capture output is written under
`artifacts/diagnostics/<run-id>/`. Dumps can contain sensitive application
memory. Pass tool-specific options after `--` where the command supports it.

## Run affected tests

```bash
./devops test -Suite affected
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
./devops aot
```

The AOT route publishes and runs a smoke executable for both supported frameworks. It is not a normal VSTest project.

## Generate coverage

```bash
./devops coverage -Clean -GenerateReport
```

Raw output is written under `coverage-results/`. Generated HTML is written under `docs/coverage/`.

Coverage proves that code executed. It does not prove that the assertions or oracle were useful.

## Run benchmarks

List suites and strategy presets:

```bash
./devops benchmark -List
```

Run a bounded smoke workload:

```bash
./devops benchmark -Suite query -Strat fast
```

Run the controlled preset:

```bash
./devops benchmark -Suite query -Controlled
```

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

The docs command copies selected repository READMEs and contribution guides into the ignored `docs/.generated` staging tree before DocFX runs. Edit the canonical repository file, not the generated site copy.

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
> Do not edit `bin`, `obj`, BenchmarkDotNet artefacts, `coverage-results` or generated documentation manually. Change their source or generator.

To add tests or tooling, continue with [CONTRIBUTING.md](CONTRIBUTING.md).
