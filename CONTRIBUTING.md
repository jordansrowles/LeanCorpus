# Contributing to LeanCorpus

Most contributions affect one part of the monorepo. You do not need to understand the complete search engine before making a useful change.

## Choose your route

| I want to work on... | Start here |
| --- | --- |
| Indexing, search, codecs, storage, mapping or diagnostics | Continue with this guide |
| Analysers, tokenisers, filters, stemmers or languages | [Contributing to Rowles.Text](src/core/Rowles.Text/CONTRIBUTING.md) |
| Tests, benchmarks, profiling or repository tooling | [Contributing to DevOps and tests](src/devops/CONTRIBUTING.md) |
| Documentation | [Documentation contributor guide](docs/contributors/documentation.md) |
| A runnable example | [Examples guide](src/examples/README.md) |
| Internal architecture | [Contributor documentation](docs/contributors/index.md) |

## Make your first change

### 1. Prepare the repository

From the repository root:

```bash
./devops setup
./devops build
```

On Windows PowerShell:

```powershell
./devops.ps1 setup
./devops.ps1 build
```

`setup` checks the development environment and required directories. `build` confirms that the normal Release configuration compiles.

> [!TIP]
> Run `./devops --help` for the current command contract. Use the repository entry point instead of assembling an undocumented sequence of `dotnet` commands.

### 2. Find the owning subsystem

Production code and its normal test area are paired:

| Production work | Implementation | Tests |
| --- | --- | --- |
| Search | `src/core/Rowles.LeanCorpus/Search/` | `src/devops/Rowles.LeanCorpus.Tests.Core/Search/` |
| Indexing and lifecycle | `src/core/Rowles.LeanCorpus/Index/` | `src/devops/Rowles.LeanCorpus.Tests.Core/Index/` |
| Storage and files | `src/core/Rowles.LeanCorpus/Store/` | `src/devops/Rowles.LeanCorpus.Tests.Core/Store/` |
| Binary codecs | `src/core/Rowles.LeanCorpus/Codecs/` | `src/devops/Rowles.LeanCorpus.Tests.Core/CodecKit/` |
| Document mapping | `src/core/Rowles.LeanCorpus/Mapping/` | `src/devops/Rowles.LeanCorpus.Tests.Core/Mapping/` |
| Text analysis | `src/core/Rowles.Text/Analysis/` | `src/devops/Rowles.Text.Tests/` |

When a change crosses boundaries, put the implementation with the subsystem that owns the behaviour and give the test every accurate `Area` trait.

### 3. Run one focused test selection

For a search change:

```bash
./devops test -Suite core -Area Search
```

For an indexing change:

```bash
./devops test -Suite core -Area Index
```

For a Rowles.Text filter:

```bash
./devops test -Suite text -Area Filters
```

A focused selection should fail for the behaviour you are changing before it passes with the implementation.

### 4. Make the change and add evidence

Keep the change coherent:

- update the owning implementation;
- add or update tests at the closest useful level;
- preserve public and on-disk compatibility unless the change intentionally alters it;
- update XML documentation and examples when public behaviour changes;
- avoid unrelated formatting or refactoring.

> [!IMPORTANT]
> Native AOT, codec formats, index metadata, file lifetimes and Windows filesystem behaviour are first-class contracts. A focused Linux unit test cannot prove a platform-specific, persistence or native-publish claim.

### 5. Run affected tests

Before handing the change over:

```bash
./devops test -Suite affected
```

Affected selection maps changed production paths through `scripts/devops/config/code-areas.psd1` and runs tests carrying the matching `Area` traits.

If a new source path or subsystem is not mapped, update `code-areas.psd1` as part of the same change.

### 6. Add the extra validation your claim needs

| Change | Additional validation |
| --- | --- |
| Public API or normal implementation | `./devops build` |
| Native AOT-sensitive path | `./devops aot` |
| Package or dependency boundary | `./devops test -Suite architecture` |
| Both target frameworks | Build with `-Framework net10.0` and `-Framework net11.0` |
| Performance claim | Relevant BenchmarkDotNet suite with controlled provenance |
| Documentation or examples | `./devops docs build -SkipBenchmarks` |
| Windows-specific behaviour | Validate on Windows and state the environment |

For a quick benchmark smoke run:

```bash
./devops benchmark -Suite query -Strat fast
```

Use `-Controlled` when comparing a repeatable, bounded workload. Do not make performance claims from a Debug build or one noisy sample.

### 7. Decide whether users need a changelog entry

Update the current release entry under `changelog/` for:

- new features or public APIs;
- changed public behaviour;
- important performance improvements;
- compatibility changes;
- significant bug fixes.

Internal clean-ups and test-only refactors normally do not need a release note.

## Test vocabulary

The detailed rules live in the [DevOps and tests guide](src/devops/CONTRIBUTING.md).

| Term | Meaning |
| --- | --- |
| Suite | The project to run, such as `core`, `text` or `architecture` |
| Category | The role of the test: `Unit`, `Integration` or `Chaos` |
| Area | The production contract protected by the test |
| Technique | How the test works, such as property-based, state-machine or metamorphic testing |

## Generated files

> [!WARNING]
> Do not manually edit `bin`, `obj`, `coverage-results`, BenchmarkDotNet artefacts or generated documentation. Change the source, configuration or generator instead.

## Before submitting

- [ ] The change solves one coherent problem.
- [ ] Tests protect observable behaviour.
- [ ] `./devops test -Suite affected` has been run.
- [ ] Compatibility and platform claims have matching evidence.
- [ ] Public documentation and examples remain accurate.
- [ ] The changelog was updated when users should know.
- [ ] Generated output was not edited directly.

For a major architectural change, discuss the design before building a large implementation around assumptions that have not been agreed.
