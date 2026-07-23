# Benchmarking

The benchmark project lives at `src/devops/Rowles.LeanCorpus.Benchmarks` and uses BenchmarkDotNet.

## Run

```powershell
./devops benchmark -Suite query -Strat fast
```

```bash
./devops benchmark -Suite query -Strat fast
```

Or directly:

```bash
dotnet run -c Release --project src/devops/Rowles.LeanCorpus.Benchmarks -- --filter '*SearchBenchmarks*'
```

## Output

Results land under `./bench/{machine}/...`. Each run writes:

| File | Contents |
|---|---|
| `report.json` | Consolidated run, suite, statistics, GC, and provenance |
| `{suite}/...` | BenchmarkDotNet markdown, CSV, and JSON |
| `../index.json` | Per-machine list of runs |

## Strategies

| Strategy | Use |
|---|---|
| `fast` | Smoke test, 500 docs, dry job |
| `quick-compare` | Short comparison, 1,000 docs |
| `intense` | Full run, 10,000 docs |
| `stress` | Larger stress run, 50,000 docs |

Pass `-Controlled` for a deterministic local diagnostic preset.

## Real data

```powershell
.\scripts\benchmark.ps1 -PrepareData -BookCount 200
```

The report records data source names, file counts, byte counts, document counts, and SHA-256 fingerprints.

## Outside a git checkout

```powershell
.\scripts\benchmark.ps1 -SourceCommit abc123 -SourceRef main -SourceManifest manifest.json
```

## Comparing runs

Diff `report.json` files between runs to surface regressions.

## See also

- [Benchmark provenance](../../articles/03-benchmark-provenance.md)
