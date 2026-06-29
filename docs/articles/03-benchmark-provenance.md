# Benchmark provenance

Each run writes a `report.json` and a per-machine `index.json` under:

```text
bench/{machine}/{yyyy-MM-dd}/{HH-mm}/
```

The report records the commit, runtime, BenchmarkDotNet version, document count, and data source fingerprints. This makes copied runs and shared benchmark folders comparable.

```powershell
.\scripts\benchmark.ps1 -Suite query -Strat fast
```

When running outside a git checkout, pass source metadata:

```powershell
.\scripts\benchmark.ps1 -SourceCommit abc123 -SourceRef main -SourceManifest manifest.json
```

## See also

- [Benchmarking](../tutorials/tips/03-benchmarking.md)
