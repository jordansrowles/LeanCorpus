---
_layout: landing
---

<section class="lc-hero" markdown="1">

<p class="lc-eyebrow">Segment-centric full-text search for .NET</p>

# LeanCorpus

**Embedded search with control over your index, query execution and deployment.**

Built for modern .NET, Native AOT and large local search workloads.

<p class="lc-hero-install"><code>dotnet add package LeanCorpus --version 4.0.0</code></p>

<p class="lc-hero-actions">
<a class="lc-button lc-button-primary" href="getting-started/01-installation.md">Build your first search</a>
<a class="lc-button" href="api/index.md">Browse the API</a>
<a class="lc-button" href="https://github.com/jordansrowles/LeanCorpus">View on GitHub</a>
</p>

</section>

> **Documentation preview:** This branch describes the unreleased LeanCorpus 4.0.0 and Rowles.Text 2.0.0 package set. The NuGet badges below show published versions, which can differ from this source. Community Server is a separate alpha track.

## Why LeanCorpus

| | |
|---|---|
| **Span-based analysis** | Ref-struct token pipeline designed to avoid per-token heap allocation during analysis. |
| **Modern scoring** | BM25 (default), BM25+, BM25L, TF-IDF, Dirichlet, Jelinek-Mercer, and SIMD-accelerated cosine. Block-Max WAND for sublinear top-k. |
| **Vector search built in** | HNSW graphs with pre/post-filtering and BBQ quantisation. Not a plugin. |
| **Native AOT** | Trim-safe, publish-ready with `PublishAot`. Validated by a dedicated smoke-test suite. |
| **LINQ queries** | `IQueryable<T>` provider translates expression trees to LeanCorpus queries. |
| **Observability built in** | OpenTelemetry tracing and metrics, slow-query log, Aspire dashboard integration. |
| **Performance evidence** | No release-qualified benchmark results are published for this preview. See the [comparison and reproducibility guidance](performance.md). |

## Start here

| | Start with this |
|---|---|
| **Build your first search** | Create an index, add documents, and run a query in [three short steps](getting-started/01-installation.md). |
| **Choose a package** | Find the core library, standalone [Rowles.Text](analysis/08-rowles-text.md), source generator and compression packages below. |
| **Learn the architecture** | Understand documents, analysis, segments, readers, queries and vectors in [Concepts](concepts/index.md). |

## Is LeanCorpus right for me?

Choose LeanCorpus when you need embedded, local .NET search with control over index files, query execution and Native AOT deployment. Choose Lucene.NET when its mature ecosystem and broad compatibility matter more than a modern .NET-first API. Choose Elasticsearch when you need a distributed service, operational tooling and multi-node scale. A database full-text feature is often simpler when search is secondary to transactional data and the required query behaviour is modest.

## Feature tour

<div class="lc-feature-grid" markdown="1">

<section class="lc-feature-card" markdown="1">

### Analysis
Span-based analysis pipeline with tokenisers, 28 token filters, 11 language stemmers, synonym graphs, Hunspell dictionary stemming, ICU, CJK tokenisation, pattern tokenisers and phonetic filters (Metaphone, Beider-Morse style).
[Browse analysis docs](analysis/index.md)

</section>

<section class="lc-feature-card" markdown="1">

### Search
25 query types including term, boolean, phrase, fuzzy (Myers bit-parallel), regexp (FST automaton), span, intervals, block-join, geo, and vector.
[Browse search docs](searching/01-query-types.md)

</section>

<section class="lc-feature-card" markdown="1">

### Scoring
BM25, BM25+, BM25L, TF-IDF (classic plus three variants), three language-model similarities, and SIMD cosine. Pluggable similarity API with expression-free function scoring.
[Browse scoring docs](searching/05-boosting-and-scoring.md)

</section>

<section class="lc-feature-card" markdown="1">

### Concurrency
Multi-threaded indexing with per-thread DWPT pools. Near-real-time readers via `SearcherManager`. Background merges that never block commits. Lease-protected segment caches.
[Browse concurrency docs](concurrency/01-searcher-manager.md)

</section>

<section class="lc-feature-card" markdown="1">

### Observability
`DefaultMetricsCollector`, `MeterMetricsCollector` (System.Diagnostics.Metrics), OpenTelemetry tracing, structured slow-query log, Aspire dashboard integration.
[Browse observability docs](observability/01-metrics.md)

</section>

<section class="lc-feature-card" markdown="1">

### Extensibility
CodecKit for custom storage formats. Pluggable compression (LZ4, Snappy, Zstandard). Source-generated document schemas. Index codec migrator for format upgrades.
[Browse contributor docs](contributors/index.md)

</section>

</div>

## Packages

| Package | Repository version | NuGet | Description |
|---|---|---|---|
| **LeanCorpus** | 4.0.0 (unreleased) | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus?style=flat)](https://www.nuget.org/packages/LeanCorpus/) | Core library. Zero dependencies. |
| **Rowles.Text** | 2.0.0 (unreleased) | [![NuGet](https://img.shields.io/nuget/v/Rowles.Text?style=flat)](https://www.nuget.org/packages/Rowles.Text/) | Standalone tokenisers, filters, stemmers and analysers. |
| **LeanCorpus.SourceGen** | 1.1.0 | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.SourceGen?style=flat)](https://www.nuget.org/packages/LeanCorpus.SourceGen/) | Roslyn source generator for typed document mapping |
| **LeanCorpus.Compression.LZ4** | 2.0.0 (unreleased) | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.LZ4?style=flat)](https://www.nuget.org/packages/LeanCorpus.Compression.LZ4/) | LZ4 stored-field compression |
| **LeanCorpus.Compression.Snappy** | 2.0.0 (unreleased) | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.Snappy?style=flat)](https://www.nuget.org/packages/LeanCorpus.Compression.Snappy/) | Snappy stored-field compression |
| **LeanCorpus.Compression.Zstandard** | 2.0.0 (unreleased) | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.Zstandard?style=flat)](https://www.nuget.org/packages/LeanCorpus.Compression.Zstandard/) | Zstandard stored-field compression |

All packages target `net10.0` and `net11.0`.

## Navigate

- [Getting started](getting-started/01-installation.md)
- [Architecture overview](architecture.md)
- [Why LeanCorpus?](why-leancorpus.md)
- [Performance](performance.md)
- [Examples](examples/index.md)
- [Feature comparison](articles/features/index.md)
- [API reference](xref:api)
- [Benchmarks](benchmarks/overview.md)

---

[![Build](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml/badge.svg)](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml)
![AOT Compatible](https://img.shields.io/badge/AOT%20Compatible-8A2BE2)
[![Docs](https://img.shields.io/badge/Docs-blue)](https://leancorpus.com)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus)
