---
_layout: landing
---

# LeanCorpus

A fast, embeddable full-text search engine for .NET. Ships as a single library. Write an index, run a query, ship your app.

```bash
dotnet add package LeanCorpus
```

[Get started](tutorials/getting-started/01-installation.md) &nbsp;|&nbsp; [API reference](~/api/index.md)

---

## What it does

| Area | Details |
|---|---|
| **Indexing** | Memory-mapped segments, BM25 scoring, index-time sort, schema validation, concurrent multi-thread indexing, CRC-protected commits |
| **Mapping** | Roslyn source generator for typed `LeanDocument` mappers, schemas, field descriptors, and stored-field materialisers |
| **Queries** | Term, boolean, phrase, prefix, wildcard, fuzzy, range, regexp, span, geo bounding box, geo distance, disjunction max |
| **Advanced queries** | HNSW vector ANN (`VectorQuery`), filtered vector search, reciprocal rank fusion (`RrfQuery`), block-join, more-like-this, function score, constant score |
| **Analysis** | Pluggable tokenisers (standard, n-gram, edge n-gram, CJK bigram), char filters, token filters, stemmers for 10+ languages |
| **Search features** | Facets, aggregations, highlighting, spell-check, [field collapsing](tutorials/advanced/09-field-collapsing.md), query cache |
| **Concurrency** | `SearcherManager` for near-real-time search, snapshot backup, configurable commit retention |
| **Operations** | `IndexValidator.Check`, `leancorpus-cli.exe check`, deep validation for DocValues, stored fields, postings, vectors, HNSW, and live docs |
| **Observability** | `ActivitySource` traces, `System.Diagnostics.Metrics`, OpenTelemetry export, slow query log, search analytics |

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
    Console.WriteLine($"doc {hit.DocId}: {fields["id"][0]} score {hit.Score}");
```

[Full walkthrough](tutorials/getting-started/01-installation.md)

---

## Example: typed mapping with the source generator

```csharp
using Rowles.LeanCorpus.Mapping.Attributes;

[LeanDocument]
public partial class Product
{
    [LeanString("id", Required = true)]
    public required string Id { get; init; }

    [LeanText("title")]
    public string? Title { get; init; }

    [LeanNumeric("price")]
    public double Price { get; init; }
}

// Index
var config = new IndexWriterConfig { Schema = ProductIndex.CreateSchema() };
using var writer = new IndexWriter(new MMapDirectory("./index"), config);
writer.AddDocument(ProductIndex.ToDocument(new Product { Id = "p1", Title = "Widget", Price = 9.99 }));
writer.Commit();

// Search with typed results
using var searcher = new IndexSearcher(new MMapDirectory("./index"));
var hits = searcher.Search(new TermQuery("title", "widget"), topN: 10);
foreach (var hit in hits.ScoreDocs)
{
    var stored = StoredDocument.Create(searcher.GetStoredFields(hit.DocId), null);
    var product = ProductIndex.FromStoredDocument(stored);
    Console.WriteLine($"{product.Id}: {product.Title} £{product.Price}");
}
```

---

## Example: vector search with HNSW

```csharp
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Queries;

var doc = new LeanDocument();
doc.Add(new StringField("id", "v1"));
doc.Add(new VectorField("embedding", new float[] { 0.1f, 0.2f, 0.3f, 0.4f }));
writer.AddDocument(doc);
writer.Commit();

var query = new VectorQuery("embedding", new float[] { 0.15f, 0.25f, 0.35f, 0.45f }, topK: 10);
var hits = searcher.Search(query, topN: 10);
// Results ranked by cosine similarity
```

---

## Why native .NET?

The index format is built for memory-mapped I/O. The query engine uses SIMD posting intersection and BlockMax WAND for early termination on large result sets. Targets `net10.0` and `net11.0`.

---

## Explore

- [Tutorials](tutorials/index.md)
- [Analysis overview](tutorials/analysis/index.md)
- [Articles](articles/index.md)
- [Index checker CLI](tutorials/index-management/04-cli-checker.md)
- [API reference](~/api/index.md)
