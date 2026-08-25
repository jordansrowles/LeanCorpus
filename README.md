# LeanCorpus Monorepo

[![Build](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml/badge.svg)](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/LeanCorpus?label=LeanCorpus)](https://www.nuget.org/packages/LeanCorpus/)
![Native AOT](https://img.shields.io/badge/Native%20AOT-compatible-8A2BE2)
[![Documentation](https://img.shields.io/badge/docs-leancorpus.com-blue)](https://leancorpus.com)
[![Changelog](https://img.shields.io/badge/changelog-releases-blue)](https://github.com/jordansrowles/LeanCorpus/tree/main/changelog)

[![3.x and 4.x Roadmap](https://img.shields.io/badge/Roadmap-3.x%20%26%204.x-0969DA?logo=github)](https://github.com/jordansrowles/LeanCorpus/discussions/52)
[![.NET 11 Plan](https://img.shields.io/badge/.NET%2011-Plan-512BD4?logo=dotnet)](https://github.com/jordansrowles/LeanCorpus/discussions/58)

LeanCorpus is an embeddable, segment-centric full-text search engine for modern .NET. It provides indexing, search, vector retrieval, typed mapping, optional compression and observability without requiring a separate search service.

> [!NOTE]
> The core package targets `net10.0` and `net11.0`. It has no external runtime dependencies and is designed for Native AOT.

## Build your first search

Install the core package:

```bash
dotnet add package LeanCorpus
```

Create an index, add a document and search it:

```csharp
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

using var directory = new MMapDirectory("./my-index");
using var writer = new IndexWriter(directory, new IndexWriterConfig());

var document = new LeanDocument();
document.Add(new StringField("id", "1"));
document.Add(new TextField("title", "The quick brown fox"));
writer.AddDocument(document);
writer.Commit();

using var searcher = new IndexSearcher(directory);
var results = searcher.Search(new TermQuery("title", "fox"), topN: 10);

Console.WriteLine($"Found {results.TotalHits} document(s).");
```

The program creates `./my-index` and prints:

```text
Found 1 document(s).
```

> [!TIP]
> Start with [Installation and first index](docs/getting-started/01-installation.md), then learn when to use each [field type](docs/getting-started/02-fields.md).

## Choose the package that matches your job

| I want to... | Package | Start here |
| --- | --- | --- |
| Embed indexing and search in a .NET application | `LeanCorpus` | [First index](docs/getting-started/01-installation.md) |
| Tokenise, analyse or stem text without the search engine | `Rowles.Text` | [Rowles.Text README](src/core/Rowles.Text/README.md) |
| Generate typed, reflection-free document mappings | `LeanCorpus.SourceGen` | [SourceGen README](src/core/Rowles.LeanCorpus.SourceGen/README.md) |
| Use fast LZ4 stored-field compression | `LeanCorpus.Compression.LZ4` | [LZ4 README](src/core/Rowles.LeanCorpus.Compression.LZ4/README.md) |
| Use Snappy stored-field compression | `LeanCorpus.Compression.Snappy` | [Snappy README](src/core/Rowles.LeanCorpus.Compression.Snappy/README.md) |
| Use Zstandard stored-field compression | `LeanCorpus.Compression.Zstandard` | [Zstandard README](src/core/Rowles.LeanCorpus.Compression.Zstandard/README.md) |

The core package already includes Deflate and Brotli. Add a compression package only when its speed or size trade-off fits your workload.

## Choose your next path

- Build a typed model with the [source generator](src/core/Rowles.LeanCorpus.SourceGen/README.md).
- Run a complete application from the [examples guide](src/examples/README.md).
- Understand documents, commits, segments and readers in the [architecture guide](docs/architecture.md).
- Add metrics, tracing and slow-query logging through [observability](docs/observability/index.md).
- Compare features and trade-offs in [Why LeanCorpus?](docs/why-leancorpus.md).
- Work on the repository using [CONTRIBUTING.md](CONTRIBUTING.md).

## Native AOT

Validate Native AOT paths in the repository with:

```bash
./devops aot
```

Optional compression packages normally register themselves through module initialisers. Native AOT applications should call the package's `Register()` method explicitly at startup.

> [!IMPORTANT]
> LeanCorpus does not support Blazor WebAssembly. Its segment-centric design requires filesystem and memory-mapped I/O. Blazor Server and Blazor Hybrid remain suitable when indexing and search run server-side.

## Monorepo map

| Path | Purpose |
| --- | --- |
| `src/core/Rowles.LeanCorpus` | Core indexing, search, storage and codec implementation |
| `src/core/Rowles.Text` | Canonical text-analysis source and standalone package |
| `src/core/Rowles.LeanCorpus.SourceGen` | Typed mapping source generator |
| `src/core/Rowles.LeanCorpus.Compression.*` | Optional compression packages |
| `src/server` | Pre-release server contracts, core and ASP.NET Core host |
| `src/devops` | Tests, benchmarks, CLI and profiling projects |
| `src/examples` | Runnable examples and end-to-end workloads |
| `docs` | DocFX documentation source |
| `lexicons` | Language data and generated lexicon assets |

## Plans and discussion

The public roadmap is developed in GitHub Discussions:

- [LeanCorpus 3.x and 4.x roadmap](https://github.com/jordansrowles/LeanCorpus/discussions/52)
- [LeanCorpus .NET 11 plan](https://github.com/jordansrowles/LeanCorpus/discussions/58)

## Quality

[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=coverage)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus)
[![Maintainability](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=sqale_rating)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus)

LeanCorpus is licensed under the repository [LICENCE](LICENCE).
