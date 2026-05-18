# Rowles.LeanCorpus

(formerly LeanLucene ([see](https://github.com/jordansrowles/LeanCorpus/issues/22)))

![NuGet Version](https://img.shields.io/nuget/v/LeanCorpus?link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FLeanCorpus%2F)
 [![Build](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml/badge.svg)](https://github.com/jordansrowles/LeanCorpus/actions/workflows/build.yml)  ![](https://img.shields.io/badge/AOT%20Compatible-8A2BE2)

Documentation: https://leancorpus.com

A .NET-native full-text search engine. Segment-centric indexing, memory-mapped reads, and atomic commit semantics. Targets `net10.0` and `net11.0`. The core library has no external dependencies; stored-field compression uses BCL types only. Optional extension packages add LZ4, Snappy, and Zstandard support.

## Projects

| Project | Description |
|---|---|
| `Rowles.LeanCorpus` | Core library |
| `Rowles.LeanCorpus.Compression.LZ4` | Optional LZ4 codec (`K4os.Compression.LZ4`) |
| `Rowles.LeanCorpus.Compression.Snappy` | Optional Snappy codec (`Snappier`) |
| `Rowles.LeanCorpus.Compression.Zstandard` | Optional Zstandard codec (`ZstdSharp.Port`) |
| `Rowles.LeanCorpus.Cli` | Command-line index checker |
| `Rowles.LeanCorpus.Tests.Shared` | Shared test helpers |
| `Rowles.LeanCorpus.Tests.Unit` | Unit test suite |
| `Rowles.LeanCorpus.Tests.Integration` | Integration test suite |
| `Rowles.LeanCorpus.Tests.Chaos` | Chaos and property-based test suite |
| `Rowles.LeanCorpus.Benchmarks` | BenchmarkDotNet suites, compared against the external benchmark baseline |
| `Rowles.LeanCorpus.Example.JsonApi` | ASP.NET Minimal API example |
| `Rowles.LeanCorpus.Example.NewsgroupsIndexer` | Console example that builds a checker-ready 20 Newsgroups index |
| `Rowles.LeanCorpus.Example.Telemetry` | OpenTelemetry traces, metrics and structured logs example |
| `Rowles.LeanCorpus.Example.NativeAot` | Native AOT smoke executable |

## Installing
```
dotnet add package LeanCorpus

# Optional

dotnet add package LeanCorpus.Compression.LZ4
dotnet add package LeanCorpus.Compression.Snappy
dotnet add package LeanCorpus.Compression.Zstandard
```

## Building and Testing

```
dotnet build
dotnet test
```

## Native AOT

`Rowles.LeanCorpus` is marked AOT-compatible for `net10.0` and `net11.0`. The core library avoids reflection-based JSON metadata and is validated by a dedicated console smoke executable rather than the ASP.NET JSON API example.

Run the local smoke check with:

```powershell
.\scripts\aot-smoke.ps1
```

This publishes `src\examples\Rowles.LeanCorpus.Example.NativeAot\Rowles.LeanCorpus.Example.NativeAot.csproj` for `win-x64` with `PublishAot=true`, then runs the native executable. The smoke executable indexes, commits, reopens, searches, reads stored fields, writes diagnostics, and exercises `FieldCompressionPolicy.None`, `FieldCompressionPolicy.Deflate`, and `FieldCompressionPolicy.Brotli`.

The core library has no native sidecar dependencies for compression. Optional packages (`Rowles.LeanCorpus.Compression.LZ4`, `Rowles.LeanCorpus.Compression.Snappy`, `Rowles.LeanCorpus.Compression.Zstandard`) may include RID-specific native binaries; AOT consumers using those packages must call their respective `Register()` methods at startup.

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
    CompressionPolicy   = FieldCompressionPolicy.Deflate,
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
| `StringField` | Exact-match keyword; not tokenised. Repeated values are also written to sorted-set DocValues for facets and deterministic grouping fallback |
| `NumericField` | `double` values; indexed in a BKD tree for range queries. Repeated values are also written to sorted-numeric DocValues for aggregations |
| `StoredField` | Stored-only string payload. Values are also written to binary DocValues so facets and grouping can avoid stored-field scans where possible |
| `GeoPointField` | Lat/lon encoded as a 64-bit integer |
| `VectorField` | `float[]` for vector/KNN queries |

Single-valued `.dvs` and `.dvn` DocValues are still written for existing sort and collapse behaviour. New `.dss`, `.dsn`, and `.dvb` sidecars are optional, so older indexes remain readable.

## Index Compatibility and CLI

Use `IndexFormatInspector` to inspect codec versions and sidecars without opening readers:

```csharp
using var dir = new MMapDirectory("./index");
var inventory = IndexFormatInspector.Inspect(dir);
```

Use `IndexCompatibility.Check` before opening user-supplied indexes:

```csharp
var compatibility = IndexCompatibility.Check(dir, new IndexCompatibilityOptions
{
    DeepValidation = true
});

if (!compatibility.CanWrite && compatibility.CanMigrate)
{
    var plan = IndexCodecMigrator.Plan(dir);
}
```

`IndexWriter`, `IndexSearcher`, and `SearcherManager` reject unsupported future formats and incomplete migration markers by default. Set `CompatibilityMode` only when intentionally opening supported older readable formats.

Use `IndexBackup` to create a manifest-backed copy of the exact files needed to restore one commit point:

```csharp
var backup = IndexBackup.Backup("./index", "./index.backup");
var restore = IndexBackup.Restore("./index.backup", "./index.restored");
```

The backup manifest records the selected `segments_N` generation, segment-owned files, optional sidecars, file lengths, and CRC-32 checksums. Restore validates the manifest before copying files and validates the restored index by default.

The CLI is built as `leancorpus-cli.exe` and exposes validation, inventory, compatibility, migration, backup, and restore commands:

```powershell
leancorpus-cli.exe check .\index --deep
leancorpus-cli.exe inspect .\index --json
leancorpus-cli.exe compat .\index --deep
leancorpus-cli.exe migrate .\index --dry-run --json
leancorpus-cli.exe migrate .\index --execute --staging .\index.migration
leancorpus-cli.exe backup .\index .\index.backup --json
leancorpus-cli.exe restore .\index.backup .\index.restored
```

See [Index checker CLI](docs/tutorials/index-management/04-cli-checker.md) for command options and exit codes.

## Analysis

The default `StandardAnalyser` lowercases, removes punctuation, applies stop word filtering, and interns tokens. Per-field analyser overrides are set on `IndexWriterConfig.FieldAnalysers`.

Built-in analysers:

- `StandardAnalyser` - configurable stop words and intern cache size
- `StemmedAnalyser` - wraps any stemmer
- `LanguageAnalyser` - language-specific pipelines
- `WhitespaceAnalyser` - splits only on whitespace, preserving punctuation and case
- `KeywordAnalyser` - treats the complete field value as a single token
- `SimpleAnalyser` - letter-only tokenisation with lowercase normalisation

Built-in stemmers: English, French, German, Spanish, Italian, Portuguese, Dutch, Russian, and Arabic.

Built-in tokenisers: standard, whitespace, keyword, letter, N-gram, edge N-gram, CJK bigram.

Built-in token filters include lowercase, stop-word removal, Porter stemming, accent folding, synonym graphs, length filtering, token truncation, unique-token filtering, elision removal, reverse-string filtering, keyword marking, decimal digit normalisation, shingles, and word delimiter splitting.

Character filters can be added to `IndexWriterConfig.CharFilters` and run before tokenisation. Token budget enforcement is configured via `MaxTokensPerDocument` and `TokenBudgetPolicy` (Truncate or Throw).

See the [Analysis tutorials](docs/tutorials/analysis/index.md) for analyser, tokeniser,
filter, and stemmer guidance, including when to use `EnglishStemmer` versus
`LightEnglishStemmer`.

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

Parses Corpus-style query strings:

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

## Index Checker

Use `IndexValidator.Check` for structured validation from application code:

```csharp
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;

using var dir = new MMapDirectory("./index");
IndexCheckResult result = IndexValidator.Check(dir, new IndexCheckOptions
{
    Deep = true
});

foreach (var issue in result.DetailedIssues)
    Console.WriteLine($"{issue.Severity} {issue.Code} {issue.SegmentId} {issue.FileName} {issue.Message}");
```

The checker reports stable issue codes, severity, file names, segment IDs, repairability flags, and suggested repair actions. Shallow validation checks commit files, segment metadata, required sidecars, codec headers, stored-field compression metadata, deletion generations, vectors, and HNSW files. Deep validation can additionally read postings, stored fields, DocValues, vectors, HNSW graphs, and live docs.

The CLI executable is `leancorpus-cli.exe`:

```powershell
dotnet build .\src\devops\Rowles.LeanCorpus.Cli\Rowles.LeanCorpus.Cli.csproj -c Release
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe check .\index --deep
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe check .\index --json --doc-values --vectors
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe check .\index --summary-only --output .\check.txt
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe migrate .\index --execute --staging .\index.migration
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe backup .\index .\index.backup
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe restore .\index.backup .\index.restored
```

Exit code `0` means healthy or warnings only, `1` means validation errors, and `2` means invalid arguments or CLI failure. `--fail-on-warnings` makes warnings return `1`. See [Index checker CLI](docs/tutorials/index-management/04-cli-checker.md).

## Benchmarks

Benchmark suites compare LeanCorpus against the external benchmark baseline across indexing, search, analysis, and more.

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

Available suites: `index`, `query`, `analysis`, `analysis-parity`, `analysis-filters`, `boolean`, `phrase`, `prefix`, `fuzzy`, `wildcard`, `deletion`, `suggester`, `schemajson`, `compound`, `indexsort`, `blockjoin`, `gutenberg-analysis`, `gutenberg-index`, `gutenberg-search`, `tokenbudget`, `diagnostics`.

Output is written to `bench/{machine}/{yyyy-MM-dd}/{HH-mm}/` with JSON, Markdown, HTML, a consolidated `report.json`, and a per-machine `index.json`.

## Example JSON API

`Rowles.LeanCorpus.Example.JsonApi` is an ASP.NET Minimal API that exposes collections over HTTP. Configure the data directory:

```
LEANCORPUS_DATA_PATH=/path/to/data
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

## Example Newsgroups Indexer

`Rowles.LeanCorpus.Example.NewsgroupsIndexer` reads the shared `bench\data\20newsgroups` corpus and writes an index suitable for exercising the checker:

```powershell
dotnet run --project .\src\examples\Rowles.LeanCorpus.Example.NewsgroupsIndexer -- --index .\artifacts\newsgroups-index --limit 500
.\src\devops\Rowles.LeanCorpus.Cli\bin\Release\net10.0\leancorpus-cli.exe check .\artifacts\newsgroups-index --deep
```

Run `.\scripts\download-news.ps1 -SkipReuters` first if the benchmark data is not present. Use `--limit` to control the number of source files indexed, `--source` to point at another 20 Newsgroups checkout, and `--append` to keep an existing index directory.
