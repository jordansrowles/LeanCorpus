# Contributor guide

This section routes repository contributors to the shortest useful guide. Package consumers should begin with the [LeanCorpus documentation](../index.md).

> [!NOTE]
> When this page links to a repository contribution guide, that repository file is the source of truth. Generated site copies should carry a header naming their source and must not be edited directly.

## Make a first contribution

1. Read the repository [CONTRIBUTING guide](../../CONTRIBUTING.md).
2. Run `./devops setup` and `./devops build`.
3. Choose the subsystem that owns the behaviour.
4. Run one focused test selection.
5. Make the change and add tests.
6. Run `./devops test -Suite affected`.
7. Add compatibility, AOT, documentation or benchmark validation when the claim requires it.

## Choose your contributor path

| I want to... | Guide |
| --- | --- |
| Make a normal LeanCorpus change | [General contribution guide](../../CONTRIBUTING.md) |
| Change analysers, filters, tokenisers, stemmers or languages | [Rowles.Text contribution guide](../../src/core/Rowles.Text/CONTRIBUTING.md) |
| Add tests, benchmarks or repository tooling | [DevOps and tests contribution guide](../../src/devops/CONTRIBUTING.md) |
| Understand `devops` and `devops.ps1` | [DevOps entry point](devops-entry-point.md) |
| Change the DocFX site or generated documentation | [Documentation](documentation.md) |
| Add or assess a benchmark | [Benchmarking](benchmarking.md) |

## Follow a subsystem into the internals

| Work | Read next |
| --- | --- |
| Trace indexing, commits, refreshes, merges or file ownership | [Architecture internals](architecture-internals.md) |
| Change a segment file, commit format, checksum or compatibility rule | [Storage formats](storage-formats.md) |
| Work on query rewriting, scoring, FSTs, BKD trees, HNSW or collectors | [Search internals](search-internals.md) |
| Add or evolve a binary codec | [CodecKit](codeckit/index.md) |

> [!IMPORTANT]
> Storage formats, Native AOT and platform-specific filesystem behaviour need validation beyond an ordinary unit test. Record the environment and the boundary actually tested.

## Repository map

| Path | Purpose |
| --- | --- |
| `src/core/Rowles.LeanCorpus` | Core indexing, search, storage, mapping and codecs |
| `src/core/Rowles.Text` | Canonical analysis source and standalone package |
| `src/core/Rowles.LeanCorpus.SourceGen` | Typed mapping source generator |
| `src/core/Rowles.LeanCorpus.Compression.*` | Optional stored-field compression |
| `src/server` | Pre-release server contracts and implementation |
| `src/devops` | Tests, benchmarks, CLI and profiling |
| `scripts/devops` | Repository automation |
| `src/examples` | Runnable examples and end-to-end workloads |
| `docs` | DocFX source and generated documentation inputs |
| `lexicons` | Language data and generated lexicon assets |

## Validate the change you made

Use the narrowest command that can disprove your claim, then widen only where needed:

```bash
./devops build
./devops test -Suite affected
./devops test -Suite architecture
./devops aot
./devops docs build -SkipBenchmarks
```

Run `./devops --help` for the current command surface.
