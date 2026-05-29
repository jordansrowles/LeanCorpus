---
_layout: landing
---

# LeanCorpus

A fast, embeddable full-text search engine for .NET. No external processes, no
JVM, no external search server. Write an index, run a query, ship your app.

```bash
dotnet add package LeanCorpus
```

[Get started](tutorials/getting-started/01-installation.md) &nbsp;|&nbsp; [API reference](~/api/index.md)

---

## What it does

| Area | Details |
|---|---|
| **Indexing** | Memory-mapped segments, BM25 scoring, index-time sorting, schema validation, concurrent multi-thread indexing, CRC-protected commits |
| **Mapping** | Optional Roslyn source generator for typed, reflection-free `LeanDocument` mappers, schemas, field descriptors, and stored-field materialisers |
| **Queries** | Term, boolean, phrase, prefix, wildcard, fuzzy, range, regexp, span, geo bounding box, geo distance, disjunction max |
| **Advanced queries** | HNSW vector ANN (`VectorQuery`), filtered vector search, reciprocal rank fusion (`RrfQuery`), block-join, more-like-this, function score, constant score |
| **Analysis** | Pluggable tokenisers (standard, n-gram, edge n-gram, CJK bigram), char filters, token filters, stemmers for 10+ languages |
| **Search features** | Facets, aggregations, highlighting, spell-check suggestions, field collapsing, query caching |
| **Concurrency** | `SearcherManager` for near-real-time search, snapshot-based backup, configurable commit retention policies |
| **Operations** | Structured `IndexValidator.Check` results, `leancorpus-cli.exe check`, deep validation for DocValues, stored fields, postings, vectors, HNSW, and live docs |
| **Observability** | `ActivitySource` traces, `System.Diagnostics.Metrics` via `MeterMetricsCollector`, structured logs through OpenTelemetry, slow query log, search analytics |

---

## Quick start

```csharp
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Search.Queries;

// Index
using var writer = new IndexWriter(new MMapDirectory("./index"), new IndexWriterConfig());

var doc = new LeanDocument();
doc.Add(new TextField("title", "The quick brown fox"));
doc.Add(new StringField("id", "1"));
writer.AddDocument(doc);
writer.Commit();

// Search
using var searcher = new IndexSearcher(new MMapDirectory("./index"));
var results = searcher.Search(new TermQuery("title", "fox"), topN: 10);

foreach (var hit in results.ScoreDocs)
{
    var fields = searcher.GetStoredFields(hit.Doc);
    Console.WriteLine(fields["id"][0]);
}
```

See the [installation tutorial](tutorials/getting-started/01-installation.md) for a full walkthrough.

---

## Why native .NET?

LeanCorpus targets .NET natively. No JNI bridge, no Java runtime dependency, no cross-process
communication. The index format is purpose-built for memory-mapped I/O on .NET, and the query
engine uses SIMD posting intersection and BlockMax WAND for early termination on large result sets.

---

## Explore

- [Tutorials](tutorials/index.md) - step-by-step guides for common tasks
- [Analysis overview](tutorials/analysis/index.md) - analysers, tokenisers, filters, and stemmer choices
- [Articles](articles/index.md) - short notes on recent features
- [Index checker CLI](tutorials/index-management/04-cli-checker.md) - command-line validation for local indexes
- [API reference](~/api/index.md) - full type and member documentation
