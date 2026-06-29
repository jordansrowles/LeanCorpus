# LeanCorpus 2.0 vs Lucene.Net 4.8

LeanCorpus benchmarks against Lucene.Net 4.8.0-beta00016 (the Java Lucene 4.8 port) as its external baseline. That version is a feature-complete port of Lucene 4.8, so several post-4.8 Lucene features (HNSW vectors, intervals, BBQ quantisation, WAND scoring) appear only on the LeanCorpus side. This comparison documents both libraries as they stand.

## 1. Core indexing and storage

| Feature | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| Inverted index (postings) | Block-based delta-encoded VarInt + FOR bit-packing | Multiple `PostingsFormat` implementations |
| Doc values (column store) | Numeric, Binary, Sorted, SortedSet, SortedNumeric | Same set via `DocValuesFormat` |
| Stored fields | Block-compressed (.fdt/.fdx); per-field compression policy | `StoredFieldsFormat` |
| Term vectors | Offsets only (.tvd/.tvx) | Positions + offsets |
| Points / BKD tree | `BKDTree` for numeric and geo range indexing | `PointValues` BKD tree |
| HNSW vector search | `HnswGraph` + `VectorQuery` | ❌ |
| Vector quantisation | BBQ (32× float32 compression) + Int8 query path | ❌ |
| Segment-centric immutable design | ✅ | ✅ |
| Two-phase commit | `CommitData` + `segments_N` | ✅ |
| Merge policies | Tiered, LogByteSize, NoMerge | Tiered, LogByteSize, LogDoc, NoMerge |
| Deletion policies | KeepLatestCommit, KeepLastNCommits | KeepOnlyLastCommit, KeepLastNCommits |
| Concurrent indexing | Per-thread `DocumentsWriterPerThread` pool | `DocumentsWriterPerThread` |
| Index sorting | `IndexSort` segment-level sort | `IndexSort` |
| NRT reader manager | `SearcherManager` | `SearcherManager` / `NRTManager` |
| Zero-allocation analysis pipeline | `SpanToken` ref struct + `ISpanTokenSink` | ❌ Heap-allocated `Token` per token |
| Memory-mapped I/O | Native `MemoryMappedFile` | `MMapDirectory` |


## 2. Query types

| Query type | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| `TermQuery` | ✅ | ✅ |
| `BooleanQuery` (Must/Should/MustNot) | ✅ | ✅ |
| `PhraseQuery` (+ Slop) | ✅ | ✅ |
| `MultiPhraseQuery` | ✅ | ✅ |
| `PrefixQuery` | FST automaton | ✅ |
| `WildcardQuery` | FST automaton | ✅ |
| `RegexpQuery` | FST automaton | ✅ |
| `FuzzyQuery` | Myers bit-parallel SWAR + Levenshtein automaton | Levenshtein |
| `TermRangeQuery` | ✅ | ✅ |
| `NumericRangeQuery` | BKD-backed | ✅ |
| `TermInSetQuery` | ✅ | ✅ |
| `ConstantScoreQuery` | ✅ | ✅ |
| `MatchAllDocsQuery` | ✅ | ✅ |
| `MatchNoDocsQuery` | ✅ | ✅ |
| `DisjunctionMaxQuery` | ✅ | ✅ |
| `SpanQuery` family (Near, Or, Not, Term) | ✅ | ✅ (core `Spans/`) |
| `BlockJoinQuery` | ✅ | ✅ (separate `Lucene.Net.Join`) |
| `FieldExistsQuery` | ✅ | ✅ |
| `MoreLikeThisQuery` | ✅ | ✅ (`Lucene.Net.Queries`) |
| `FunctionScoreQuery` (Multiply/Replace/Sum/Max) | ✅ | ❌ |
| `CombinedFieldsQuery` (multi-field term centric) | ✅ | ❌ |
| `IntervalsQuery` | ✅ | ❌ |
| `RrfQuery` (Reciprocal Rank Fusion) | ✅ | ❌ |
| `VectorQuery` (HNSW + pre/post filter) | ✅ | ❌ |
| `QueryParser` (classic syntax) | ✅ | ✅ |
| Flexible QueryParser | ❌ | ✅ `Lucene.Net.QueryParser.Flexible` |

## 3. Scoring models

| Similarity | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| BM25 (default) | `Bm25Similarity` | `BM25Similarity` |
| Classic TF-IDF | `TfIdfSimilarity` + 3 variants | `ClassicSimilarity` |
| BM25+ | `Bm25PlusSimilarity` | ❌ |
| BM25L | `Bm25LSimilarity` | ❌ |
| Dirichlet (Language Model) | `DirichletSimilarity` | ✅ (via `SimilarityBase`) |
| Jelinek-Mercer (LM) | `LMJelinekMercerSimilarity` | ✅ |
| Absolute Discounting (LM) | `LMAbsoluteDiscountingSimilarity` | ✅ |
| Pluggable similarity API | Interface-based, stateless | Abstract class |
| Block-Max WAND scoring | `BlockMaxWandScorer` | ❌ |
| SIMD-accelerated cosine | `SimdCosineSimilarity` | ❌ |
| Expression-based scoring | ❌ | ✅ `Lucene.Net.Expressions` (JS expressions) |

## 4. Filters, facets, sorting, highlighting

| Feature | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| Filters | Via `Query` tree + `RoaringBitmap` | `Filter` + `DocIdSet` |
| Sorting | `SortField` + `TopNCollector` | `Sort`/`SortField`/`FieldComparator` |
| Result collapsing | `CollapseField` / `CollapseMode` | `Lucene.Net.Grouping` |
| Faceted search | `FacetsCollector` flat per-field counts | `Lucene.Net.Facet` taxonomy + drill-down |
| Hierarchical facets (drill-down) | ❌ | `TaxonomyReader` + `DrillDownQuery` |
| Aggregations (min/max/sum/count/avg + histogram) | `NumericAggregator` | ❌ |
| Highlighting | 4 strategies via `IHighlighter`: plain, postings, term-vector, hybrid | `Lucene.Net.Highlighter` (multiple formatters) |
| Query cache | `QueryCache` | `QueryCache` |

## 5. Tokenisers

| Tokeniser | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| `StandardTokenizer` | `Tokeniser` | ✅ |
| `WhitespaceTokenizer` | ✅ | ✅ |
| `LetterTokenizer` | ✅ | ✅ |
| `KeywordTokenizer` | ✅ | ✅ |
| `NGramTokenizer` | ✅ | ✅ |
| `EdgeNGramTokenizer` | ✅ | ✅ |
| `CJKBigramFilter` | `CJKBigramTokeniser` | ✅ |
| `ThaiTokenizer` | ✅ | ✅ |
| `ICUTokenizer` | `IcuTokeniser` | ✅ (`Lucene.Net.ICU`) |
| `UAX29URLEmailTokenizer` | `Uax29UrlEmailTokeniser` | ✅ |
| `PatternTokeniser` (regex split) | ✅ | ❌ |
| `MediaWikiTokeniser` | ✅ | ❌ |
| `PathHierarchyTokenizer` | ❌ | ✅ |
| `ClassicTokenizer` | ❌ | ✅ |

## 6. Token filters

| Filter | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| `LowercaseFilter` | ✅ | ✅ |
| `StopFilter` | `StopWordFilter` | ✅ |
| `PorterStemFilter` | `PorterStemmerFilter` | ✅ |
| `SnowballFilter` | ❌ (language-specific stemmers used instead) | ✅ |
| `ShingleFilter` | ✅ | ✅ |
| `LengthFilter` | ✅ | ✅ |
| `TypeTokenFilter` | ✅ | ✅ |
| `UniqueTokenFilter` | ✅ | ✅ |
| `ReverseStringFilter` | ✅ | ✅ |
| `WordDelimiterFilter` | ✅ | ✅ |
| `HyphenatedWordsFilter` | ✅ | ✅ |
| `ElisionFilter` | ✅ | ✅ |
| `CommonGramsFilter` | ✅ | ✅ |
| `TruncateTokenFilter` | ✅ | ✅ |
| `LimitTokenCountFilter` | ✅ | ✅ |
| `DecimalDigitFilter` | ✅ | ✅ |
| `MappingCharFilter` | ✅ | ✅ |
| `PatternReplaceFilter` | ✅ | ✅ |
| `PatternReplaceCharFilter` | ✅ | ✅ |
| `HtmlStripCharFilter` | ✅ | ✅ |
| `FlattenGraphFilter` | ✅ | ✅ |
| `KeepWordFilter` | ✅ | ✅ |
| `KeywordMarkerFilter` | ✅ | ✅ |
| `CachingTokenFilter` | ✅ | ✅ |
| `HunspellStemFilter` | ✅ | ✅ (`Lucene.Net.Analysis.Common`) |
| `AccentFoldingFilter` | ✅ | ❌ (separate `Lucene.Net.Analysis.Phonetic`) |
| `MetaphoneFilter` | ✅ | ❌ (separate `Lucene.Net.Analysis.Phonetic`) |
| `PhoneticAlternatesFilter` | ✅ | ❌ (separate package) |
| `SynonymFilter` | ✅ | ✅ |
| `KStemFilter` | `KStemmer` | ✅ |
| `DictionaryStemmer` (Morfologik) | ❌ | ✅ |

## 7. Language stemmers

| Language | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| English | Porter, LightEnglish, KStem | Porter, KStem, Snowball |
| Arabic | ✅ | Snowball |
| Chinese | ✅ | ❌ (CJK analysis only) |
| Dutch | ✅ | Snowball |
| French | ✅ | Snowball |
| German | ✅ | Snowball |
| Italian | ✅ | Snowball |
| Japanese | ✅ | ❌ (Kuromoji separate) |
| Korean | ✅ | ❌ |
| Portuguese | ✅ | Snowball |
| Russian | ✅ | Snowball |
| Spanish | ✅ | Snowball |
| Swedish | ✅ | Snowball |
| Turkish | ❌ | Snowball |
| Hindi | ❌ | Snowball |
| Hungarian | ❌ | Snowball |
| Romanian | ❌ | Snowball |
| Hunspell dictionary | `HunspellDictionary` + `HunspellStemFilter` | `Lucene.Net.Analysis.Common` |

## 8. Codecs and compression

| Feature | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| LZ4 compression | `K4os.Compression.LZ4` | `Lucene.Net.Codecs.LZ4` |
| Snappy compression | `Snappier` | ❌ |
| Zstandard compression | `ZstdSharp.Port` | ❌ |
| Custom codec API | `Codec` abstract + registration | `Codec` abstract + `CodecFactory` |
| Term dictionary | FST transducer | FST + BlockTree |
| Skip list in postings | Block skip index (interval 128) | Multiple skip list formats |
| Streaming postings merge | `StreamingPostingsMerger` | `StreamingMergeScheduler` |
| Index format migration | `IndexCodecMigrator` | ❌ |

## 9. Document model and schema

| Feature | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| Document container | `LeanDocument` bag of `IField` | `Document` list of `IIndexableField` |
| Field types | String, Text, Numeric, Binary, Vector, Stored, GeoPoint | String, Text, Numeric, Binary, Stored, Int32/Int64/Single/Double |
| Field validation | `FieldNameValidator`, `FieldBoostValidator` | ❌ manual |
| Schema validation | `IndexSchema` field mapping + strict mode | ❌ manual |
| Source-generated schemas | Roslyn incremental generator from `[LeanDocument]` attributes | ❌ |
| LINQ query support | `LeanQueryable<T>`, `LeanExpressionVisitor`, `IQueryable<T>` | ❌ |
| JSON serialisation | Direct document to/from JSON | ❌ |

## 10. Spatial and geo

| Feature | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| Geo-point field | `GeoPointField` | ❌ (separate `Lucene.Net.Spatial`) |
| BKD-backed geo queries | Range + distance bounding | `PointValues` for geo |
| Full spatial module (shapes, WKT, distance) | ❌ | `Lucene.Net.Spatial` |
| Spatial4n integration | ❌ | ✅ |

## 11. Suggestions and spellcheck

| Feature | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| Spell index | `SpellIndex` | `Lucene.Net.Suggest` |
| Did-you-mean suggester | `DidYouMeanSuggester` | `SpellChecker` |
| Fuzzy suggester | Myers/Levenshtein automaton | ✅ |
| Analysing suggester | ❌ | ✅ |
| FreeText suggester | ❌ | ✅ |
| Completion suggester | ❌ | `Lucene.Net.Search.Suggest` |

## 12. Platform and ecosystem

| Feature | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| Target frameworks | `net10.0`, `net11.0` | `net462`, `netstandard2.0`, `net6.0` |
| Native AOT | `PublishAot` trim-safe | ❌ |
| `SkipLocalsInit` | Assembly-wide | ❌ |
| OpenTelemetry | Built-in tracing + metrics | ❌ |
| CLI tool | `leancorpus-cli` (System.CommandLine) | ❌ |
| Source generator | Roslyn incremental (`Rowles.LeanCorpus.SourceGen`) | ❌ |
| Property-based testing | FsCheck chaos testing | ❌ |
| NuGet package ID | `LeanCorpus` | `Lucene.Net` |

## 13. Unique to Lucene.Net 4.8

Features present in Lucene.Net 4.8 that LeanCorpus does not have.

| Feature | Description |
|---|---|
| `Lucene.Net.Spatial` | Full spatial module: shapes, WKT, distance queries, Spatial4n integration |
| `Lucene.Net.Facet` | Taxonomy-based hierarchical faceting with drill-down |
| `Lucene.Net.Expressions` | JavaScript expression-based scoring (`DoubleValuesSource`) |
| `Lucene.Net.Classification` | KNN document classifier |
| `Lucene.Net.QueryParser.Flexible` | Framework-based extensible query parser |
| `Lucene.Net.Analysis.Phonetic` | Soundex, RefinedSoundex, Caverphone, DaitchMokotoff, DoubleMetaphone |
| `Lucene.Net.Misc` | Diversified top docs, doc values with updates, misc utilities |
| Snowball stemmers (full set) | 15+ languages via Snowball |
| `PathHierarchyTokenizer` | Tokeniser for file-path-like hierarchical input |
| `ClassicTokenizer` | Legacy Lucene 2.x tokeniser |
| Kuromoji (Japanese) | Morphological analyser for Japanese (separate package) |
| SmartCN (Chinese) | Simplified Chinese analyser |
| Completion suggester | Prefix-based autocomplete with weights |
| Analysing / FreeText suggester | Analysis-backed suggestions |
| `DiversifiedTopDocsCollector` | Result diversification |
| `BlockPackedWriter` monotonic | Optimised monotonic block packing |
| `OrdinalMap` | Multi-segment ordinal mapping for sorted doc values |
| `LiveFieldValues` | Reference-counted GC-free field values |

## 14. Unique to LeanCorpus 2.0

Features present in LeanCorpus 2.0 that Lucene.Net 4.8 does not have.

| Feature | Description |
|---|---|
| HNSW vector search | Approximate nearest neighbour via Hierarchical Navigable Small World graph with pre/post-filter |
| BBQ quantisation | Better Binary Quantisation: 32× compression of float32 vectors with int8 query path |
| Block-Max WAND | Sublinear top-k retrieval for multi-term queries |
| BM25+ / BM25L | Advanced BM25 variants with lower-bound and document-length normalisation |
| IntervalsQuery | Positional interval matching |
| RRF (Reciprocal Rank Fusion) | Merge multiple query result sets |
| Zero-allocation analysis | `SpanToken` ref struct + `ISpanTokenSink`; no per-token heap allocation |
| LINQ query support | `IQueryable<T>` provider with expression tree translation |
| Source-generated schemas | Roslyn incremental generator from `[LeanDocument]` POCOs |
| SIMD-accelerated cosine | Vectorised cosine similarity for dense vectors |
| CombinedFieldsQuery | Multi-field term-centric scoring |
| Native AOT | Trim-safe, publish-ready without JIT |
| OpenTelemetry | Built-in distributed tracing and metrics |
| JSON document serialisation | Direct `LeanDocument` to/from `System.Text.Json` |
| Index codec migrator | Automatic index format upgrade between versions |
| MediaWiki tokeniser | Purpose-built for Wikipedia/MediaWiki markup |
| Pattern tokeniser | Regex-based tokenisation |
| Accent folding | Built-in |
| Metaphone / PhoneticAlternates | Built-in phonetic filters |
| NumericAggregator | Built-in min/max/sum/count/avg + histogram bucketing |
| Result collapsing | Built-in |
| CLI tool (`leancorpus-cli`) | Index inspection, check, diagnostics |
| FsCheck chaos testing | Property-based testing suite |
| Multi-targeting `net10.0` + `net11.0` | Latest .NET with performance improvements |

## 15. Summary

| Dimension | LeanCorpus 2.0 | Lucene.Net 4.8 |
|---|---|---|
| Search features | More modern (HNSW, WAND, Intervals, RRF) | Expressions, Flexible QP, Completion |
| Scoring models | More (BM25+, BM25L, SIMD cosine) | Classic + LM |
| Vector search | First-class | ❌ |
| Analysis pipeline | Zero-allocation, built-in phonetic | Heap-allocated, phonetic separate package |
| Ecosystem breadth | Smaller, focused | Larger (Spatial, Facet taxonomy, Classification) |
| Platform | Modern .NET only (net10/11, AOT) | Broad .NET (net462+) but no AOT |
| Tooling | CLI, source gen, OpenTelemetry | No CLI/source gen |
| Language stemmers | ~13 custom stemmers + Hunspell | Snowball (15+) + SmartCN + Kuromoji + Hunspell |
| Document model | LINQ + JSON + source gen | Classic mutable Document |
| Compression codecs | LZ4 + Snappy + Zstandard | LZ4 only |


## See also

- [Benchmark provenance](03-benchmark-provenance.md)
- [Codecs](05-codecs.md)
- [HNSW vector search](02-hnsw-vector-search.md)
- [Benchmarking guide](../tutorials/tips/03-benchmarking.md)
- [Stored field compression](../tutorials/tips/01-compression.md)
