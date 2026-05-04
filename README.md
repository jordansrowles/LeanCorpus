# Rowles.LeanLucene

![NuGet Version](https://img.shields.io/nuget/v/LeanLucene?style=flat-square) [![Build](https://github.com/jordansrowles/LeanLucene/actions/workflows/build.yml/badge.svg)](https://github.com/jordansrowles/LeanLucene/actions/workflows/build.yml)

A .NET-native full-text search engine. Segment-centric indexing, memory-mapped reads, and atomic commit semantics. Targets `net10.0` and `net11.0`. The only external dependency for the core library is [NativeCompressions](https://www.nuget.org/packages/NativeCompressions) (LZ4 + Zstandard). Everything else uses BCL types.

## Projects

| Project | Description |
|---|---|
| `Rowles.LeanLucene` | Core library |
| `Rowles.LeanLucene.Tests` | xUnit test suite |
| `Rowles.LeanLucene.Benchmarks` | BenchmarkDotNet suites, compared against Lucene.NET |
| `Rowles.LeanLucene.Example.JsonApi` | ASP.NET Minimal API example |
| `Rowles.LeanLucene.Example.Telemetry` | OpenTelemetry traces, metrics and structured logs example |

## Building and Testing

```
dotnet build
dotnet test
```

## Quick Start

```csharp
var dir = new MMapDirectory("path/to/index");
var config = new IndexWriterConfig();

using var writer = new IndexWriter(dir, config);

var doc = new LeanDocument();
doc.Add(new TextField("title", "hello world", stored: true));
doc.Add(new StringField("id", "1", stored: true));
doc.Add(new StoredField("source", "readme"));
writer.AddDocument(doc);
writer.Commit();

using var searcher = new IndexSearcher(dir);
var results = searcher.Search("hello", "title", topN: 10);
```

For near-real-time search, use `SearcherManager`, which polls for new commits and swaps the searcher with reference-counted acquire/release:

```csharp
using var mgr = new SearcherManager(dir);
var searcher = mgr.Acquire();
try   { var results = searcher.Search("hello", "title", 10); }
finally { mgr.Release(searcher); }
```

## IndexWriter

Buffers documents in memory and flushes immutable segments to disk. Auto-flushes when `RamBufferSizeMB` (default 256 MB) or `MaxBufferedDocs` (default 10,000) is reached. Background segment merges run after each commit.

```csharp
var config = new IndexWriterConfig
{
    RamBufferSizeMB     = 128,
    MaxBufferedDocs     = 5_000,
    MaxQueuedDocs       = 10_000,         // backpressure; blocks AddDocument when exceeded
    CompressionPolicy   = FieldCompressionPolicy.Lz4,
    StoredFieldBlockSize = 16,
    MergeThreshold      = 10,
    PostingsSkipInterval = 128,
    StoreTermVectors    = false,
    UseCompoundFile     = false,
    IndexSort           = new IndexSort("date", SortFieldType.Long, reverse: true),
    Schema              = mySchema,       // optional; validates fields on AddDocument
    DeletionPolicy      = new KeepLastNCommitsPolicy(3),
    Metrics             = new DefaultMetricsCollector(),
};
```

### Document Updates and Deletes

```csharp
// Atomic delete-then-add
writer.UpdateDocument("id", "42", replacement);

// Soft delete
writer.DeleteDocuments(new TermQuery("id", "42"));
writer.Commit();
```

### Block-Join Indexing

Index parent/child document blocks for nested queries:

```csharp
writer.AddDocumentBlock(new[] { child1, child2, parentDoc });
```

## Field Types

| Type | Description |
|---|---|
| `TextField` | Tokenised text; supports analysis pipeline |
| `StringField` | Exact-match keyword; not tokenised |
| `NumericField` | `double` values; indexed in a BKD tree for range queries |
| `GeoPointField` | Lat/lon encoded as a 64-bit integer |
| `VectorField` | `float[]` for vector/KNN queries |

## Analysis

The default `StandardAnalyser` lowercases, removes punctuation, applies stop word filtering, and interns tokens. Per-field analyser overrides are set on `IndexWriterConfig.FieldAnalysers`.

Built-in analysers:

- `StandardAnalyser` - configurable stop words and intern cache size
- `StemmedAnalyser` - wraps any stemmer
- `LanguageAnalyser` - language-specific pipelines

Built-in stemmers: English, French, German, Spanish, Italian, Portuguese, Dutch, Russian, and Arabic.

Built-in tokenisers: standard, N-gram, edge N-gram, CJK bigram.

Character filters can be added to `IndexWriterConfig.CharFilters` and run before tokenisation. Token budget enforcement is configured via `MaxTokensPerDocument` and `TokenBudgetPolicy` (Truncate or Throw).

## Queries

| Query | Notes |
|---|---|
| `TermQuery` | Single exact term |
| `BooleanQuery` | Combines clauses with Must / Should / MustNot |
| `PhraseQuery` | Ordered term sequence; slop supported |
| `PrefixQuery` | `term*` |
| `WildcardQuery` | `te?m*` |
| `FuzzyQuery` | Edit-distance matching |
| `RangeQuery` / `TermRangeQuery` | Numeric and string range |
| `RegexpQuery` | FST-backed regexp matching |
| `VectorQuery` | KNN by cosine similarity |
| `MoreLikeThisQuery` | Document similarity |
| `FunctionScoreQuery` | Custom per-doc score function |
| `DisjunctionMaxQuery` | Best-scoring clause wins |
| `ConstantScoreQuery` | Wraps any query with a fixed score |
| `RrfQuery` | Reciprocal rank fusion |
| `SpanNearQuery` / `SpanOrQuery` / `SpanNotQuery` | Span-level proximity |
| `BlockJoinQuery` | Parent/child nested document queries |
| `GeoBoundingBoxQuery` / `GeoDistanceQuery` | Geographic filtering |

### Query Parser

Parses Lucene-style query strings:

```csharp
var parser = new QueryParser("content", new StandardAnalyser());
var query = parser.Parse("+title:lean -status:deleted \"full text\"~2 fuzzy~1 prefix* field:value^2.0");
```

Supported syntax: `field:term`, `"phrase"`, `"slop phrase"~N`, `+required`, `-excluded`, `(grouping)`, `prefix*`, `wild?card`, `fuzzy~N`, `term^boost`.

### BooleanQueryBuilder

```csharp
var query = new BooleanQueryBuilder()
    .Must(new TermQuery("status", "active"))
    .Should(new TermQuery("category", "tech"))
    .MustNot(new TermQuery("deleted", "true"))
    .Build();
```

## Scoring

Default similarity is BM25 (`Bm25Similarity.Instance`). TF-IDF is also available. The scoring model is set on both `IndexWriterConfig.Similarity` and `IndexSearcherConfig.Similarity`. Multi-segment searches use BlockMaxWAND for early termination. `IndexSort` controls segment order at flush time.

Score explanations:

```csharp
var explanation = searcher.Explain(new TermQuery("title", "lean"), docId);
```

## Aggregations and Facets

```csharp
var agg = new AggregationRequest("price", AggregationType.Histogram, interval: 10.0);
var result = searcher.Aggregate(query, agg);

var facets = searcher.GetFacets(query, "category", topN: 10);
```

## Suggestions

```csharp
var suggestions = DidYouMeanSuggester.Suggest(searcher, "title", "worl", maxEdits: 2, topN: 5);
```

## Highlights

```csharp
var highlighter = new Highlighter(searcher, query);
string snippet = highlighter.GetBestFragment("content", storedText);
```

## Field Collapsing

Deduplicate results by a field value:

```csharp
var collapse = new CollapseField("thread_id", CollapseMode.TopScore);
var results = searcher.SearchWithCollapse(query, topN: 10, collapse);
```

## Per-query Resource Controls

Bound query latency and intermediate memory with `SearchOptions`. Limits are checked between segments; on early termination `TopDocs.IsPartial` is set.

```csharp
var opts = new SearchOptions
{
    Timeout        = TimeSpan.FromMilliseconds(50),
    MaxResultBytes = 1 * 1024 * 1024,
};
var results = searcher.Search(query, topN: 10, opts);
if (results.IsPartial) { /* hit deadline or budget */ }

foreach (var hit in searcher.SearchStreaming(query, perSegmentTopN: 1024, opts))
{
    // segment-by-segment results, in segment order
}
```

## Diagnostics

```csharp
var searcherConfig = new IndexSearcherConfig
{
    Metrics       = new DefaultMetricsCollector(),
    SlowQueryLog  = new SlowQueryLog(threshold: TimeSpan.FromMilliseconds(50)),
    SearchAnalytics = new SearchAnalytics(capacity: 1000),
};

var writerConfig = new IndexWriterConfig
{
    Metrics = new DefaultMetricsCollector(),
};

var snapshot = ((DefaultMetricsCollector)searcherConfig.Metrics).GetSnapshot();
IndexSizeReport size = searcher.GetIndexSize();
```

## Index Snapshots

Point-in-time read-only views of the index, safe to hold while the writer continues indexing:

```csharp
IndexSnapshot snap = writer.AcquireSnapshot();
// ... use snap ...
writer.ReleaseSnapshot(snap);
```

## Schema Validation

```csharp
var schema = new IndexSchema();
schema.AddField("id",    FieldType.String,  required: true);
schema.AddField("title", FieldType.Text,    required: true);
schema.AddField("price", FieldType.Numeric, required: false);

var config = new IndexWriterConfig { Schema = schema };
```

`SchemaValidationException` is thrown from `AddDocument` on violation.

## Deletion Policies

| Policy | Description |
|---|---|
| `KeepLatestCommitPolicy` | Keeps only the most recent commit (default) |
| `KeepLastNCommitsPolicy` | Keeps the last N commit generations |

## Index Recovery

On construction, `IndexWriter` reads the latest `segments_N` file and loads any existing commit state. Partial or corrupt commits are skipped.

## Benchmarks

Benchmark suites compare LeanLucene against Lucene.NET across indexing, search, analysis, and more.

```powershell
# All suites, full run
.\scripts\benchmark.ps1

# Single suite, smoke test
.\scripts\benchmark.ps1 -Suite query -Strat fast

# Intense run, specific doc count
.\scripts\benchmark.ps1 -Strat intense -DocCount 20000

# List available suites
.\scripts\benchmark.ps1 -List
```

Available suites: `index`, `query`, `analysis`, `boolean`, `phrase`, `prefix`, `fuzzy`, `wildcard`, `deletion`, `suggester`, `schemajson`, `compound`, `indexsort`, `blockjoin`, `gutenberg-analysis`, `gutenberg-index`, `gutenberg-search`, `tokenbudget`, `diagnostics`.

Output is written to `bench/{machine}/{yyyy-MM-dd}/{HH-mm}/` with JSON, Markdown, HTML, a consolidated `report.json`, and a per-machine `index.json`.

## Example JSON API

`Rowles.LeanLucene.Example.JsonApi` is an ASP.NET Minimal API that exposes collections over HTTP. Configure the data directory:

```
LEANLUCENE_DATA_PATH=/path/to/data
```

Endpoints:

```
GET    /collections
DELETE /collections/{name}
POST   /collections/{name}/documents   (body: JSON object or array)
DELETE /collections/{name}/documents?field=id&term=42
GET    /collections/{name}/search?q=hello&field=content&topN=10
```

Search responses include `totalHits`, `hits` (score + stored fields), and `suggestions` (DidYouMean per token).
