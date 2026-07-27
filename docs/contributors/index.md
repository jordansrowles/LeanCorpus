# Contributor guide

This section explains the internal contracts behind LeanCorpus and the workflows used to change them safely. It is aimed at people working on the repository rather than applications consuming the NuGet packages.

## Start here

| Area | Use it when |
|---|---|
| [Architecture internals](architecture-internals.md) | You need to trace indexing, commits, refreshes, merges, or file ownership |
| [Storage formats](storage-formats.md) | You are changing a segment file, commit format, checksum, or compatibility rule |
| [Search internals](search-internals.md) | You are working on query rewriting, scoring, FSTs, BKD trees, HNSW, bitsets, or collectors |
| [Benchmarking](benchmarking.md) | You need to add a benchmark or assess a performance change |
| [Documentation](documentation.md) | You are changing the DocFX site, diagrams, API comments, or generated reference material |
| [DevOps entry point](devops-entry-point.md) | You need to understand or extend `devops` and `devops.ps1` |
| [CodecKit](codeckit/index.md) | You are adding or evolving a binary codec |

## Repository map

| Path | Purpose |
|---|---|
| `src/core/Rowles.LeanCorpus` | Core indexing, storage, codec, analysis, and search implementation |
| `src/core/Rowles.LeanCorpus.Compression.*` | Optional stored-field compression providers |
| `src/core/Rowles.LeanCorpus.SourceGen` | Roslyn source generator |
| `src/devops/Rowles.LeanCorpus.Cli` | Command-line maintenance tools |
| `src/devops/Rowles.LeanCorpus.Tests.*` | Unit, integration, chaos, AOT, and codec parity tests |
| `src/devops/Rowles.LeanCorpus.Benchmarks*` | BenchmarkDotNet suites and corpus workloads |
| `src/examples` | Usage examples and Native AOT samples |
| `docs` | DocFX source, templates, diagrams, and generated reports |

## Change discipline

Storage changes require an explicit compatibility decision. Search changes require correctness tests before performance measurements. Public API changes should update XML comments and the relevant guide. Native AOT compatibility is part of the normal contract, not a separate optional target.

Use the repository entry point for validation:

```powershell
./devops build
./devops test
./devops aot
./devops docs -SkipBenchmarks
```

Choose the narrowest command that covers the change, then run broader validation only when the affected boundary requires it.
