# Migration from Lucene.Net

LeanCorpus shares Lucene's segment-centric architecture and many API concepts, but it is a fresh implementation, not a fork. This guide maps the most common Lucene.Net patterns to their LeanCorpus equivalents.

## Package and namespace

| Concept | Lucene.Net | LeanCorpus |
|---|---|---|
| Package | `Lucene.Net` | `LeanCorpus` |
| Root namespace | `Lucene.Net` | `Rowles.LeanCorpus` |
| Target frameworks | `net462`, `netstandard2.0`, `net6.0` | `net10.0`, `net11.0` |

## Directory and I/O

```csharp
// Lucene.Net
var dir = FSDirectory.Open("/path/to/index");

// LeanCorpus
using var dir = new MMapDirectory("/path/to/index");
```

LeanCorpus uses memory-mapped I/O exclusively. `MMapDirectory` is the only built-in directory implementation. No `FSDirectory`, `RAMDirectory`, or `NRTCachingDirectory`.

## IndexWriter

```csharp
// Lucene.Net
var config = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyser);
var writer = new IndexWriter(dir, config);

// LeanCorpus
var config = new IndexWriterConfig
{
    Analyser = analyser,
    RamBufferSizeMB = 256,
    MergePolicy = new TieredMergePolicy(),
};
using var writer = new IndexWriter(dir, config);
```

Key differences:
- `IndexWriterConfig` is a mutable POCO with sensible defaults, not a constructor-based config.
- `Commit()` commits all pending changes. `PrepareCommit()` stages a commit that can be rolled back with `Rollback()` — use for atomic multi-document operations.
- `Dispose()` commits and closes by default. Use `Rollback()` to discard uncommitted changes.
- Merges never block commits in LeanCorpus.

## Documents and fields

```csharp
// Lucene.Net
var doc = new Document();
doc.Add(new StringField("id", "1", Field.Store.YES));
doc.Add(new TextField("body", "hello world", Field.Store.NO));

// LeanCorpus
var doc = new LeanDocument();
doc.Add(new StringField("id", "1", stored: true));
doc.Add(new TextField("body", "hello world"));
```

| Lucene.Net field | LeanCorpus field | Notes |
|---|---|---|
| `StringField` | `StringField` | Not analysed. Stored is opt-in. |
| `TextField` | `TextField` | Analysed. Stored is opt-in. |
| `Int32Field` / `Int64Field` / `SingleField` / `DoubleField` | `NumericField` | Single numeric field type for all numeric types. |
| `StoredField` | `StoredField` | Identical. |
| `BinaryDocValuesField` / `SortedDocValuesField` / etc. | DocValues are implicit | DocValues are populated automatically for indexed fields. Set `StoreDocValues = false` to opt out. |
| -- | `VectorField` | Dense float vectors. No Lucene.Net 4.8 equivalent. |
| -- | `GeoPointField` | Latitude/longitude. No Lucene.Net 4.8 equivalent (requires `Lucene.Net.Spatial`). |
| -- | `BinaryField` | Raw byte arrays. |

## Analysis

```csharp
// Lucene.Net
var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);

// LeanCorpus
var analyser = new StandardAnalyser();
```

| Lucene.Net | LeanCorpus |
|---|---|
| `StandardAnalyzer` | `StandardAnalyser` |
| `WhitespaceAnalyzer` | Custom: `new Analyser(new WhitespaceTokenizer())` |
| `StopAnalyzer` | Custom: `new Analyser(new Tokeniser(), new StopWordFilter(StopWords.English))` |
| `EnglishAnalyzer` (Porter) | `StemmedAnalyser` |
| `KeywordAnalyzer` | `new Analyser(new KeywordTokenizer())` |
| `LowerCaseFilter` | `LowercaseFilter` |
| `PorterStemFilter` | `PorterStemmerFilter` |
| `SynonymFilter` | `SynonymGraphFilter` |
| `ShingleFilter` | `ShingleFilter` |

Analysers, tokenisers, and filters are registered in a constructor chain:

```csharp
var analyser = new Analyser(
    tokeniser: new Tokeniser(),
    new LowercaseFilter(),
    new StopWordFilter(StopWords.English));
```

There is no `Analyzer.TokenStream` pattern. The analysis pipeline pushes tokens through an `ISpanTokenSink` using ref structs.

## Searching

```csharp
// Lucene.Net
var reader = DirectoryReader.Open(dir);
var searcher = new IndexSearcher(reader);
var query = new TermQuery(new Term("body", "hello"));
var topDocs = searcher.Search(query, 10);

// LeanCorpus
using var searcher = new IndexSearcher(dir);
var query = new TermQuery("body", "hello");
var topDocs = searcher.Search(query, topN: 10);
```

| Lucene.Net | LeanCorpus |
|---|---|
| `IndexSearcher(DirectoryReader)` | `IndexSearcher(MMapDirectory)` or `IndexSearcher(IndexReader)` |
| `searcher.Search(query, n)` | `searcher.Search(query, topN: n)` |
| `TopDocs` | `TopDocs` (same concept, different struct) |
| `ScoreDoc` | `Hit` in `TopDocs.ScoreDocs` |
| `TermQuery(Term)` | `TermQuery(string field, string term)` |
| `BooleanQuery` | `BooleanQuery` (same builder API) |
| `PhraseQuery` with `Slop` | `PhraseQuery` with `Slop` |
| `PrefixQuery` | `PrefixQuery` |
| `FuzzyQuery` | `FuzzyQuery` (uses Myers bit-parallel SWAR, not Levenshtein) |
| `WildcardQuery` | `WildcardQuery` |
| `NumericRangeQuery` | `RangeQuery` (BKD-backed) |

## NRT and searcher management

```csharp
// Lucene.Net
var tracker = new IndexWriter(...);
var nrtReader = DirectoryReader.Open(tracker, true);
var nrtManager = new NRTManager(tracker, searcherFactory);

// LeanCorpus
using var manager = new SearcherManager(dir, new SearcherManagerConfig
{
    RefreshInterval = TimeSpan.FromSeconds(1),
});
using var lease = manager.AcquireLease();
var hits = lease.Searcher.Search(query, 10);
```

LeanCorpus uses `SearcherManager` as the single NRT reader abstraction. It handles background refresh, lease tracking, and safe disposal of old searchers.

## Deletions and updates

```csharp
// Lucene.Net
writer.DeleteDocuments(new Term("id", "abc-123"));
writer.UpdateDocument(new Term("id", "abc-123"), doc);

// LeanCorpus -- identical API
writer.DeleteDocuments(new TermQuery("id", "abc-123"));
writer.UpdateDocument(new TermQuery("id", "abc-123"), doc);
```

LeanCorpus uses `Query` objects (not Lucene.Net `Term`) for delete and update targeting. Any query type works.

## What LeanCorpus does not have

These Lucene.Net features have no LeanCorpus equivalent and no migration path:

- `Lucene.Net.Spatial` (full spatial module with shapes and WKT)
- `Lucene.Net.Facet` (taxonomy-based drill-down facets)
- `Lucene.Net.Expressions` (JavaScript expression scoring)
- `Lucene.Net.Classification` (KNN document classifier)
- `Lucene.Net.QueryParser.Flexible` (framework-based query parser)
- `Lucene.Net.Analysis.Phonetic` (separate package; LeanCorpus has built-in Metaphone and accent folding)
- `RAMDirectory` and `NRTCachingDirectory`
- Snowball stemmers (LeanCorpus uses custom Snowball-inspired stemmers instead)
- Completion, analysing, and FreeText suggesters
