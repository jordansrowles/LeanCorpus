# Benchmarking

The benchmark project lives at `src/devops/Rowles.LeanCorpus.Benchmarks` and uses
BenchmarkDotNet.

## Run

Use the wrapper scripts for normal runs:

```powershell
.\scripts\benchmark.ps1 -Suite query -Strat fast
```

```bash
./scripts/benchmark.sh --suite query --strat fast
```

They set the output layout and metadata. You can still run BenchmarkDotNet
directly:

```bash
dotnet run -c Release --project src/devops/Rowles.LeanCorpus.Benchmarks -- --filter '*'
```

Filter to a specific class:

```bash
dotnet run -c Release --project src/devops/Rowles.LeanCorpus.Benchmarks -- --filter '*SearchBenchmarks*'
```

## Output layout

Results land under `./bench/{machine}/...`, where `{machine}` is the local
hostname. This keeps results from different machines from overwriting each other
when the directory is shared via source control.

The corpus and any generated indices used by the benchmarks also live under
`./bench`.

Each run writes:

| File | Contents |
|---|---|
| `report.json` | Consolidated run, suite, statistics, GC and provenance data |
| `{suite}/...` | BenchmarkDotNet markdown, CSV and JSON output |
| `../index.json` | Per-machine list of runs |

## Strategies

| Strategy | Use |
|---|---|
| `fast` | Smoke test, 500 docs, dry job |
| `quick-compare` | Short comparison, 1,000 docs |
| `intense` | Full run, 10,000 docs |
| `stress` | Larger stress run, 50,000 docs |

Pass `-Controlled` or `--controlled` for a deterministic local diagnostic preset.

## Suite shape

Suites that mix unrelated costs are split at the runner level. The `blockjoin`
alias runs `blockjoin-index` and `blockjoin-search`, and the `deletion` alias
runs `deletion-queue` and `deletion-commit`. Use the specific suite names when
you need one table per workload.

Boolean and fuzzy suites use deterministic scenario names rather than broad
single-word labels. This keeps each row tied to a specific query shape.

## Real data

Use `-PrepareData` or `--prepare-data` to fetch data if it is missing:

```powershell
.\scripts\benchmark.ps1 -PrepareData -BookCount 200
```

The report records data source names, file counts, byte counts, document counts
and SHA-256 fingerprints.

## Copied source trees

If the benchmark source has no `.git` folder, pass provenance explicitly:

```powershell
.\scripts\benchmark.ps1 -SourceCommit abc123 -SourceRef main -SourceManifest manifest.json
```

```bash
./scripts/benchmark.sh --source-commit abc123 --source-ref main --source-manifest manifest.json
```

## Comparing runs

BenchmarkDotNet writes `Markdown`, `CSV`, and `JSON` reports per run. Diff JSON
files between runs to surface regressions. Prefer `report.json` when comparing
whole runs, because it includes the benchmark data fingerprint.
