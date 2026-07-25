# Why LeanCorpus?

LeanCorpus is a .NET-native full-text search engine designed for applications that need fast, embeddable search with no external service dependencies. It is not a distributed system, not a REST API, and not a managed cloud service. It is a library you add to your project.

## When to use LeanCorpus

**You are building a .NET application that needs search.** Desktop apps, CLI tools, ASP.NET services, background workers. Add the NuGet package and you have search.

**You need predictable, low-latency queries.** Segment files are memory-mapped. Hot queries hit the OS page cache. No network round trips, no serialisation overhead, no JVM warmup.

**You care about allocation pressure.** The analysis pipeline uses ref structs (`SpanToken`) and value-type enumerators throughout. Scoring loops avoid boxing and LINQ allocations. Benchmarked allocation per `TermQuery` is under 1 KB.

**You deploy with Native AOT.** The core library is trim-safe and validated with a dedicated AOT smoke-test suite. Publish as a single native binary with no JIT.

**You want modern scoring and vectors.** BM25+, BM25L, language-model similarities, HNSW vector search with BBQ quantisation, and Block-Max WAND are built in. These are not plugins or add-ons.

**You need observability.** OpenTelemetry tracing and metrics, structured slow-query logging, and Aspire dashboard integration are first-class features.

**You already use .NET 10 or later.** LeanCorpus targets `net10.0` and `net11.0` only. It leverages `SkipLocalsInit`, ref structs, `SearchValues`, and other modern runtime features.

## When not to use LeanCorpus

**You need a distributed search cluster.** LeanCorpus is a library, not a service. If you need sharding, replication, or a REST API out of the box, use Elasticsearch or a similar distributed engine. (You can build these on top of LeanCorpus, but it is not provided.)

**You are on .NET Framework or .NET 8 or earlier.** Not supported and not planned.

**You need Blazor WASM.** LeanCorpus requires a filesystem for memory-mapped I/O. Blazor Server and Blazor Hybrid work fine, as long as indexing happens server-side.

**You need spatial search with shapes and WKT.** LeanCorpus has geo-point fields (lat/lon) with bounding-box and distance queries, but no full spatial module (polygons, WKT parsing, Spatial4n integration).

**You need hierarchical faceted search with drill-down.** LeanCorpus has flat field-level faceting and result collapsing. Taxonomy-based drill-down facets are not implemented.

**You need completion suggesters or analysing suggesters.** The spell-check and did-you-mean suggesters are built in. Prefix-based autocomplete and analysis-backed suggestions are not.

## LeanCorpus vs Lucene.Net

LeanCorpus benchmarks against Lucene.Net 4.8 as its external baseline. Both are segment-centric engines with similar mental models (IndexWriter, IndexSearcher, analyser pipelines), but they differ in focus:

| | LeanCorpus | Lucene.Net 4.8 |
|---|---|---|
| **Platform** | `net10.0`, `net11.0` | `net462`, `netstandard2.0`, `net6.0` |
| **AOT** | Native AOT, trim-safe | Not supported |
| **Allocation** | Zero-allocation span-based analysis | Per-token heap allocation |
| **Vectors** | HNSW + BBQ/Int8 quantisation, built in | Not available |
| **Scoring** | BM25+, BM25L, WAND, SIMD cosine | Classic + LM, expression-based scoring |
| **Modern queries** | Intervals, RRF, CombinedFields, FunctionScore | Not available |
| **Observability** | OpenTelemetry, metrics, slow-query log | Not available |
| **Stemmers** | 11 language stemmers + Hunspell (CJK are no-op adapters) | 15+ Snowball + SmartCN + Kuromoji |

The [feature comparison](articles/vs-lucene.md) covers every feature category in detail.

LeanCorpus is not a Lucene.Net replacement for every use case. It is a fresh implementation that prioritises modern .NET performance, allocation control, and AOT compatibility over ecosystem breadth.

## LeanCorpus vs Elasticsearch

Elasticsearch is a distributed search service built on Lucene. LeanCorpus is a library.

- **Deployment**: Elasticsearch runs as a separate process or cluster. LeanCorpus runs in-process.
- **Scale**: Elasticsearch handles sharding, replication, and cluster coordination. LeanCorpus operates on a single index directory.
- **Operations**: Elasticsearch has REST APIs, Kibana, beats, and a management ecosystem. LeanCorpus has a CLI tool for index inspection.
- **Performance**: Elasticsearch pays network and serialisation overhead on every query. LeanCorpus reads memory-mapped files directly.

If you need a search cluster, use Elasticsearch. If you need embedded search in a .NET application, use LeanCorpus.

## LeanCorpus vs SQL Server Full-Text Search

SQL Server FTS is integrated with the database engine and uses the SQL query language. LeanCorpus is a standalone library.

- **Query language**: SQL Server FTS uses `CONTAINS` and `FREETEXT` predicates. LeanCorpus has a classic query parser plus a type-safe `IQueryable<T>` LINQ provider.
- **Scoring**: SQL Server FTS uses a proprietary ranking algorithm (RANK). LeanCorpus uses BM25 (default) with several alternatives.
- **Flexibility**: SQL Server FTS tokenisation and stemming are configured at the server level. LeanCorpus gives you per-field analyser chains.
- **Deployment**: SQL Server FTS requires SQL Server. LeanCorpus works with any storage backend.

Choose SQL Server FTS when your data already lives in SQL Server and your search needs are simple. Choose LeanCorpus when you need control over the analysis pipeline, modern scoring, vector search, or AOT deployment.
