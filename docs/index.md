---
_layout: landing
---

# LeanCorpus

**A fast, embeddable full-text search engine for modern .NET. Zero dependencies, AOT-ready, segment-centric design.**

```bash
dotnet add package LeanCorpus
```

## Five-minute quick start

```csharp
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

// Create an index
using var dir = new MMapDirectory("./my-index");
using var writer = new IndexWriter(dir, new IndexWriterConfig());

// Add documents
var doc = new LeanDocument();
doc.Add(new TextField("title", "The quick brown fox"));
doc.Add(new StringField("id", "1"));
writer.AddDocument(doc);
writer.Commit();

// Search
using var searcher = new IndexSearcher(dir);
var hits = searcher.Search(new TermQuery("title", "fox"), topN: 10);

foreach (var hit in hits.ScoreDocs)
    Console.WriteLine($"docId={hit.DocId}  score={hit.Score:F3}");
```

## Why LeanCorpus

| | |
|---|---|
| **Zero allocations in the hot path** | Ref-struct token pipeline. No per-token heap allocation during analysis or scoring. |
| **Modern scoring** | BM25 (default), BM25+, BM25L, TF-IDF, Dirichlet, Jelinek-Mercer, and SIMD-accelerated cosine. Block-Max WAND for sublinear top-k. |
| **Vector search built in** | HNSW graphs with pre/post-filtering and BBQ quantisation (32x compression). Not a plugin. |
| **Native AOT** | Trim-safe, publish-ready with `PublishAot`. Validated by a dedicated smoke-test suite. |
| **LINQ queries** | `IQueryable<T>` provider translates expression trees to LeanCorpus queries. |
| **Observability built in** | OpenTelemetry tracing and metrics, slow-query log, Aspire dashboard integration. |
| **Competitive performance** | Benchmarks against Lucene.Net 4.8 on 100k-document corpora. Up to 63x faster highlighting, 33x faster geo-distance queries, with 10x to 100x less allocation. |

## Feature tour

<div markdown="1" class="row">

<div markdown="1" class="col-md-6 mb-3">
<div markdown="1" class="card h-100">
<div markdown="1" class="card-body">

### Analysis
Zero-allocation span-based pipeline: tokenisers, 28 token filters, 11 language stemmers, synonym graphs, Hunspell dictionary stemming, ICU, CJK tokenisation, pattern tokenisers, phonetic filters (Metaphone, Beider-Morse style).
[Browse analysis docs](analysis/index.md)

</div>
</div>
</div>

<div markdown="1" class="col-md-6 mb-3">
<div markdown="1" class="card h-100">
<div markdown="1" class="card-body">

### Search
25 query types including term, boolean, phrase, fuzzy (Myers bit-parallel), regexp (FST automaton), span, intervals, block-join, geo, and vector.
[Browse search docs](searching/01-query-types.md)

</div>
</div>
</div>

<div markdown="1" class="col-md-6 mb-3">
<div markdown="1" class="card h-100">
<div markdown="1" class="card-body">

### Scoring
BM25, BM25+, BM25L, TF-IDF (classic plus three variants), three language-model similarities, and SIMD cosine. Pluggable similarity API with expression-free function scoring.
[Browse scoring docs](searching/05-boosting-and-scoring.md)

</div>
</div>
</div>

<div markdown="1" class="col-md-6 mb-3">
<div markdown="1" class="card h-100">
<div markdown="1" class="card-body">

### Concurrency
Multi-threaded indexing with per-thread DWPT pools. Near-real-time readers via `SearcherManager`. Background merges that never block commits. Lease-protected segment caches.
[Browse concurrency docs](concurrency/01-searcher-manager.md)

</div>
</div>
</div>

<div markdown="1" class="col-md-6 mb-3">
<div markdown="1" class="card h-100">
<div markdown="1" class="card-body">

### Observability
`DefaultMetricsCollector`, `MeterMetricsCollector` (System.Diagnostics.Metrics), OpenTelemetry tracing, structured slow-query log, Aspire dashboard integration.
[Browse observability docs](observability/01-metrics.md)

</div>
</div>
</div>

<div markdown="1" class="col-md-6 mb-3">
<div markdown="1" class="card h-100">
<div markdown="1" class="card-body">

### Extensibility
CodecKit for custom storage formats. Pluggable compression (LZ4, Snappy, Zstandard). Source-generated document schemas. Index codec migrator for format upgrades.
[Browse contributor docs](contributors/index.md)

</div>
</div>
</div>

</div>

## Packages

| Package | NuGet | Description |
|---|---|---|
| **LeanCorpus** | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus?style=flat)](https://www.nuget.org/packages/LeanCorpus/) | Core library. Zero dependencies. |
| **Rowles.Text** | [![NuGet](https://img.shields.io/nuget/v/Rowles.Text?style=flat)](https://www.nuget.org/packages/Rowles.Text/) | Standalone tokenisers, filters, stemmers and analysers. |
| **LeanCorpus.SourceGen** | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.SourceGen?style=flat)](https://www.nuget.org/packages/LeanCorpus.SourceGen/) | Roslyn source generator for typed document mapping |
| **LeanCorpus.Compression.LZ4** | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.LZ4?style=flat)](https://www.nuget.org/packages/LeanCorpus.Compression.LZ4/) | LZ4 stored-field compression |
| **LeanCorpus.Compression.Snappy** | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.Snappy?style=flat)](https://www.nuget.org/packages/LeanCorpus.Compression.Snappy/) | Snappy stored-field compression |
| **LeanCorpus.Compression.Zstandard** | [![NuGet](https://img.shields.io/nuget/v/LeanCorpus.Compression.Zstandard?style=flat)](https://www.nuget.org/packages/LeanCorpus.Compression.Zstandard/) | Zstandard stored-field compression |

All packages target `net10.0` and `net11.0`.

## Navigate

- [Getting started](getting-started/01-installation.md)
- [Architecture overview](architecture.md)
- [Why LeanCorpus?](why-leancorpus.md)
- [Performance](performance.md)
- [Feature comparison](articles/features/index.md)
- [API reference](~/api/index.md)
- [Benchmarks](benchmarks/index.md)

---

[![Build](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml/badge.svg)](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml)
![AOT Compatible](https://img.shields.io/badge/AOT%20Compatible-8A2BE2)
[![Docs](https://img.shields.io/badge/Docs-blue)](https://leancorpus.com)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=jordansrowles_LeanCorpus&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=jordansrowles_LeanCorpus)
