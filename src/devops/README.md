# DevOps projects and test infrastructure

This directory contains LeanCorpus development tooling, test projects, benchmarks and profiling tools. The repository entry point is [`./devops`](../../devops), not direct ad hoc `dotnet` command sequences. It selects the repository SDK, exports `DOTNET_ROOT`, and dispatches to the scripts in [`scripts/devops`](../../scripts/devops).

## Project map

| Project | Purpose |
| --- | --- |
| `Rowles.LeanCorpus.Cli` | Command-line tooling used by users, examples and integration tests. |
| `Rowles.LeanCorpus.Tests.Core` | Main LeanCorpus unit, integration and chaos suite. Covers core indexing, search, codecs, storage, mapping, LINQ, diagnostics and Core to Rowles.Text integration. |
| `Rowles.Text.Tests` | Unit and integration tests for analysers, filters, tokenisers, stemmers, dictionaries and language support. |
| `Rowles.LeanCorpus.Tests.SourceGen` | Roslyn source-generator tests. Warnings are errors in this project. |
| `Rowles.LeanCorpus.Tests.Architecture` | Architectural-boundary tests using ArchUnitNET. |
| `Rowles.LeanCorpus.Tests.AOTSmoke` | Native AOT smoke executable. It is published and run, rather than discovered by `dotnet test`. |
| `Rowles.LeanCorpus.Tests.Shared` | xUnit-agnostic fixtures, historical index fixtures and test infrastructure reused by test projects. It is not itself a test project. |
| `Rowles.LeanCorpus.Benchmarks` | Core BenchmarkDotNet workloads. |
| `Rowles.LeanCorpus.Benchmarks.Compression` | Compression-codec benchmarks. |
| `Rowles.Text.Benchmarks` | Analysis and language benchmarks. |
| `Rowles.LeanCorpus.Profiling` | Profiling entry points and diagnostic workloads. |

## Test organisation

`Rowles.LeanCorpus.Tests.Core` is organised by production area, then by technique. For example:

```text
Index/
  Unit/          focused component behaviour
  Integration/   writer, reader, merge, recovery and on-disk journeys
  Chaos/         generated, corrupted, hostile or stateful inputs
    StateMachine/ model-based FsCheck machines and their harnesses
Search/
CodecKit/
Store/
Document/
...
```

Use the closest production area. A test that crosses an important subsystem boundary belongs in `Integration`; it should not be called end-to-end merely because it creates an `IndexWriter` and `IndexSearcher` in the same process.

## Metadata and selection

Tests declare a singular `Category`, one or more `Area` traits and, where useful, one or more `Technique` traits from [`Rowles.LeanCorpus.Tests.Core/Metadata/TestMetadata.cs`](Rowles.LeanCorpus.Tests.Core/Metadata/TestMetadata.cs):

| Category | Use |
| --- | --- |
| `Unit` | Small, deterministic component behaviour. |
| `Integration` | Observable behaviour spanning production components, files or process boundaries. |
| `Chaos` | Generated data, corruption, property testing, state machines and hostile conditions. |

`Area` identifies the subsystem, such as `Index`, `Search`, `CodecKit`, `Store`, `TextIntegration`, `Analysers` or `Stemmers`. Keep traits accurate because `./devops test -Suite affected` maps changed production paths to `suite:area` targets using [`scripts/devops/config/code-areas.psd1`](../../scripts/devops/config/code-areas.psd1).

The standard suites are defined in [`scripts/devops/config/test-suites.psd1`](../../scripts/devops/config/test-suites.psd1):

```powershell
./devops test                         # core, text, sourcegen, architecture and AOT
./devops test -Suite core
./devops test -Suite affected
./devops test -Suite core -Area Index -Category Chaos
./devops test -Suite core -Filter 'FullyQualifiedName~StateMachine'
```

`-Filter` is passed to the test runner. `-Area` and `-Category` are converted to trait filters and can be combined with it.

## Test techniques and oracles

Prefer an oracle that is independent of the implementation decision being exercised.

| Technique | Where to look | Expected oracle |
| --- | --- | --- |
| Unit | `*/Unit` | Direct contract, boundary or error assertion. |
| Integration | `*/Integration` | Observable index, API or filesystem result. Use stored logical IDs, not internal document IDs. |
| Chaos and FsCheck | `*/Chaos` | Invariant, reference model, round-trip property, corruption rejection or bounded fallback. |
| State machines | `Index/Chaos/StateMachine` | A simple immutable model and an isolated harness owning its directory, writer and readers. Each operation must describe itself for FsCheck shrinking. |
| Metamorphic/equivalence | `Index/Chaos/Metamorphic`, `Tests.Shared/Metamorphic/MetamorphicRelations.cs`, `Index/Integration/WriterEquivalenceTests.cs`, `MergeEquivalenceTests.cs` | Compare equivalent executions, for example sequential versus concurrent indexing or unmerged versus merged segments. Compare logical results, not only a hit count, where the contract permits. |
| Native AOT | `Rowles.LeanCorpus.Tests.AOTSmoke` | Publish the executable for each target framework and RID, then run it. |

Do not use a shared `ChaosDirectoryFixture` for a state machine. A machine owns its environment so a shrunk trace remains isolated and reproducible. Keep a clear distinction between generated-input fuzzing, model-based histories, metamorphic transformations and systematic concurrency: they expose different failures.

### Metamorphic testing

Metamorphic tests prove a relation between multiple executions when a single expected result is impractical or would duplicate the implementation. They belong under the owning production area in `Chaos/Metamorphic` and use both `Technique(PropertyBased)` and `Technique(Metamorphic)`.

The shared relation layer is [`Rowles.LeanCorpus.Tests.Shared/Metamorphic/MetamorphicRelations.cs`](Rowles.LeanCorpus.Tests.Shared/Metamorphic/MetamorphicRelations.cs). `MetamorphicObservation` captures ordered logical IDs and stored fields. `MetamorphicRelations.Holds` then evaluates one of these relations:

| Relation | Meaning |
| --- | --- |
| `Exact`, `OrderedEquivalent`, `RoundTrip` | Ordered logical IDs and stored fields are identical. |
| `SetEquivalent`, `Idempotent`, `Commutative` | Logical ID set and stored fields are identical; hit ordering may differ. |
| `MonotonicSubset` | The transformed result is a subset of the baseline. |
| `Approximate` | Result counts differ by no more than the specified tolerance. |

Use stored, stable IDs as observations. Do not compare internal document IDs, segment names, scores, timing, or incidental hit order unless that order is itself the relation under test. The first examples are [`Index/Chaos/Metamorphic/IndexMetamorphicTests.cs`](Rowles.LeanCorpus.Tests.Core/Index/Chaos/Metamorphic/IndexMetamorphicTests.cs): sequential versus concurrent ingestion is set-equivalent, and force-merge preserves logical results exactly.

## Running, coverage and generated output

Use the narrowest useful command. The normal validation commands are:

```powershell
./devops build
./devops test -Suite core -Area Index
./devops aot
./devops coverage -GenerateReport
./devops benchmarks -List
```

`./devops test` includes the AOT route. `./devops aot` publishes and runs the smoke executable for `net10.0` and `net11.0`; it is not a VSTest suite.

Coverage discovers the conventional test projects under this directory, excluding `Tests.Shared`, `Tests.AOTSmoke` and benchmark projects. It writes raw data to `coverage-results/` and, when requested, writes the ReportGenerator site to `docs/coverage/`. Generated source under `obj/` is excluded. These outputs, plus `bin/`, `obj/`, benchmark results and generated documentation, must not be edited manually.

## Adding or changing tests

1. Put the test in the project and area that own the production contract.
2. Add `Category` and `Area` metadata.
3. Reuse `Tests.Shared` only for framework-agnostic infrastructure and fixtures.
4. Keep historical fixture names and embedded-resource names stable.
5. If a production area is new, update `code-areas.psd1` so `test affected` cannot silently miss it.
6. Run the smallest relevant suite and record any platform limitation precisely.

Native AOT, codec compatibility, corruption recovery and Windows filesystem behaviour are first-class concerns. A green Linux unit test is not evidence for a platform-specific or native-publish claim.
