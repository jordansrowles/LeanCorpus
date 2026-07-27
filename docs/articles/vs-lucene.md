# Lucene Feature Parity

`✔` means a direct equivalent is available
`◐` means a broadly comparable capability
`❌` means no equivalent

Lucene.NET refers to the currently packaged 4.8.0-beta00016 line
Lucene (Java) refers to Lucene 10.3.1,

---

## Text Analysis 

### Analysers

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Standard analyser | ✔   `StandardAnalyser` | ✔ | ✔ | Lucene: `StandardAnalyzer` |
| Whitespace analyser | ✔   `WhitespaceAnalyser` | ✔ | ✔ | Lucene: `WhitespaceAnalyzer` |
| Simple analyser | ✔   `SimpleAnalyser` | ✔ | ✔ | Lucene: `SimpleAnalyzer`; letter-only and lowercase |
| Keyword analyser | ✔   `KeywordAnalyser` | ✔ | ✔ | Lucene: `KeywordAnalyzer`; single-token passthrough |
| Stemmed analyser | ✔   `StemmedAnalyser` | ◐ | ◐ | Wraps any `IStemmer`; Lucene composes an `Analyzer` with stemming filters. |
| Language analyser (per-language pipelines) | ✔   `LanguageAnalyser` | ✔ | ✔ | Lucene language-specific `Analyzer` implementations; EN, FR, DE, ES, IT, PT, NL, RU, AR, ZH, JA, KO |
| ICU analyser (ICU tokenisation) | ✔   `IcuAnalyser` / `IcuTokeniser` | ✔ | ✔ | Unicode segmenter-backed |
| Custom analyser composition | ✔   `Analyser` / `AnalyserFactory` | ✔ | ✔ | |
| Per-field analyser overrides | ✔   `IndexWriterConfig.FieldAnalysers` | ✔ | ✔ | |
| Token budget / truncation policy | ✔   `MaxTokensPerDocument` / `TokenBudgetPolicy` | ❌ | ❌ | Truncates or throws when a document exceeds its index-time token limit. |
| Character-level filters (applied before tokenisation) | ✔   `IndexWriterConfig.CharFilters` | ✔ | ✔ | Ordered character-filter pipeline before analyser tokenisation. |
| Span-based analysis API | ✔   `ISpanTokeniser` / `ISpanTokenFilter` / `ISpanTokenSink` | ❌ | ❌ | Low-allocation, span-based token processing surface. |

---

### Tokenisers

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Standard tokeniser | ✔   `Tokeniser` | ✔ | ✔ | Lucene: `StandardTokenizer` |
| Whitespace tokeniser | ✔   `WhitespaceTokeniser` | ✔ | ✔ | Lucene: `WhitespaceTokenizer` |
| Keyword tokeniser | ✔   `KeywordTokeniser` | ✔ | ✔ | Lucene: `KeywordTokenizer` |
| Letter tokeniser | ✔   `LetterTokeniser` | ✔ | ✔ | Lucene: `LetterTokenizer` |
| N-gram tokeniser | ✔   `NGramTokeniser` | ✔ | ✔ | Lucene: `NGramTokenizer` |
| Edge n-gram tokeniser | ✔   `EdgeNGramTokeniser` | ✔ | ✔ | Lucene: `EdgeNGramTokenizer` |
| CJK bigram tokeniser | ✔   `CJKBigramTokeniser` | ✔ | ✔ | Lucene: `CJKBigramTokenizer` |
| Chinese lexicon tokeniser | ✔   `ChineseLexiconTokeniser` | ✔ | ✔ | Greedy longest-match segmentation with unigram fallback |
| Japanese morphological tokeniser | ✔   `JapaneseTokeniser` | ✔ | ✔ | LeanCorpus `.jlc` dictionary and least-cost Viterbi path |
| ICU tokeniser (Unicode segmenter) | ✔   `IcuTokeniser` / `UnicodeTokenisation` | ✔ | ✔ | |
| Thai tokeniser | ✔   `ThaiTokeniser` | ✔ | ✔ | Lucene: `ThaiTokenizer` |
| UAX29 URL/email tokeniser | ✔   `Uax29UrlEmailTokeniser` | ✔ | ✔ | Lucene: `UAX29URLEmailTokenizer` |
| Wikipedia tokeniser | ✔   `MediaWikiTokeniser` | ✔ | ✔ | Lucene: `WikipediaTokenizer` |
| Pattern tokeniser | ✔   `PatternTokeniser` | ✔ | ✔ | Lucene: `PatternTokenizer` |
| Path-hierarchy tokeniser | ❌ | ✔ | ✔ | Lucene: `PathHierarchyTokenizer` |
| Classic tokeniser | ❌ | ✔ | ✔ | Lucene: legacy `ClassicTokenizer` |

---

### Token Filters

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| LowercaseFilter | ✔   `LowercaseFilter` | ✔ | ✔ | |
| StopFilter | ✔   `StopWordFilter` | ✔ | ✔ | |
| PorterStemFilter | ✔   `PorterStemmerFilter` | ✔ | ✔ | |
| AccentFoldingFilter (ASCIIFoldingFilter) | ✔   `AccentFoldingFilter` | ✔ | ✔ | |
| SynonymGraphFilter | ✔   `SynonymGraphFilter` + `SynonymMap` | ✔ | ✔ | |
| LengthFilter | ✔   `LengthFilter` | ✔ | ✔ | |
| TruncateTokenFilter | ✔   `TruncateTokenFilter` | ✔ | ✔ | |
| UniqueTokenFilter | ✔   `UniqueTokenFilter` | ✔ | ✔ | |
| DecimalDigitFilter | ✔   `DecimalDigitFilter` | ✔ | ✔ | |
| ReverseStringFilter | ✔   `ReverseStringFilter` | ✔ | ✔ | |
| ElisionFilter | ✔   `ElisionFilter` | ✔ | ✔ | French elision |
| KeywordMarkerFilter | ✔   `KeywordMarkerFilter` | ✔ | ✔ | |
| ShingleFilter | ✔   `ShingleFilter` | ✔ | ✔ | |
| WordDelimiterGraphFilter | ✔   `WordDelimiterFilter` | ✔ | ✔ | |
| FlattenGraphFilter | ✔   `FlattenGraphFilter` | ✔ | ✔ | |
| KeepWordFilter | ✔   `KeepWordFilter` | ✔ | ✔ | |
| LimitTokenCountFilter | ✔   `LimitTokenCountFilter` | ✔ | ✔ | |
| TypeTokenFilter | ✔   `TypeTokenFilter` | ✔ | ✔ | |
| HunspellStemFilter | ✔   `HunspellStemFilter` + `HunspellDictionary` | ✔ | ✔ | |
| Metaphone phonetic filter | ✔   `MetaphoneFilter` | ✔ | ✔ | |
| Phonetic alternates (Beider-Morse style) | ✔   `PhoneticAlternatesFilter` + `PhoneticEncoding` | ✔ | ✔ | Emits bounded phonetic expansions at same position |
| ClassicFilter | ✔   `ClassicFilter` | ✔ | ✔ | |
| CachingTokenFilter | ✔   `CachingTokenFilter` | ✔ | ✔ | |
| CommonGramsFilter | ✔   `CommonGramsFilter` | ✔ | ✔ | |
| HyphenatedWordsFilter | ✔   `HyphenatedWordsFilter` | ✔ | ✔ | |
| PatternReplaceFilter | ✔   `PatternReplaceFilter` | ✔ | ✔ | |
| Generic stem token filter | ✔   `StemTokenFilter` / `SnowballStemmer` | ✔ | ✔ | Lucene: `SnowballFilter` |
| Morfologik dictionary stemmer | ❌ | ✔ | ✔ | Lucene: `MorfologikFilter` / `DictionaryStemmer` |
| Normalisation filters (Arabic, German, Hindi, Indic) | ❌ | ✔ | ✔ | Backlog |

---

### Character Filters

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| HTMLStripCharFilter | ✔   `HtmlStripCharFilter` | ✔ | ✔ | |
| MappingCharFilter | ✔   `MappingCharFilter` | ✔ | ✔ | |
| PatternReplaceCharFilter | ✔   `PatternReplaceCharFilter` | ✔ | ✔ | |

---

### Stemmers

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| English (Porter / Snowball) | ✔   `EnglishStemmer` | ✔ | ✔ | |
| Light English (minimal) | ✔   `LightEnglishStemmer` | ✔ | ✔ | Krovetz-inspired light |
| KStem (English) | ✔   `KStemmer` + `KStemLexicon` | ✔ | ✔ | Krovetz stemmer |
| French | ✔   `FrenchStemmer` | ✔ | ✔ | |
| German | ✔   `GermanStemmer` | ✔ | ✔ | |
| Spanish | ✔   `SpanishStemmer` | ✔ | ✔ | |
| Italian | ✔   `ItalianStemmer` | ✔ | ✔ | |
| Portuguese | ✔   `PortugueseStemmer` | ✔ | ✔ | |
| Dutch | ✔   `DutchStemmer` | ✔ | ✔ | |
| Russian | ✔   `RussianStemmer` | ✔ | ✔ | |
| Arabic | ✔   `ArabicStemmer` | ✔ | ✔ | |
| Chinese | ❌ | ✔ | ✔ | `ChineseStemmer` is a no-op compatibility adapter that returns input unchanged. |
| Japanese | ❌ | ✔ | ✔ | `JapaneseStemmer` is a no-op compatibility adapter that returns input unchanged. |
| Korean | ❌ | ✔ | ✔ | `KoreanStemmer` is a no-op compatibility adapter that returns input unchanged. |
| Hindi | ❌ | ✔ | ✔ | Backlog |
| Turkish | ❌ | ✔ | ✔ | Backlog |

---

## Field Types / Document Model

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Document model | ✔   `LeanDocument` | ✔ | ✔ | |
| TextField | ✔   `TextField` | ✔ | ✔ | Tokenised; the two-argument constructor stores by default. |
| StringField | ✔   `StringField` | ✔ | ✔ | Exact match, sorted-set DocValues sidecar |
| NumericField | ✔   `NumericField` | ✔ | ✔ | BKD-indexed, sorted-numeric DocValues sidecar |
| Int64Field | ✔   `Int64Field` | ◐ | ✔ | Dedicated signed 64-bit field; Lucene.NET uses its older numeric field APIs. |
| StoredField | ✔   `StoredField` | ✔ | ✔ | Stored-only, binary DocValues sidecar |
| VectorField | ✔   `VectorField` | ❌ | ✔ | `float[]` for HNSW/kNN; Lucene.NET 4.8 has no vector-search API. |
| Byte-vector field | ❌ | ❌ | ✔ | Lucene (Java): `KnnByteVectorField`. |
| Float and double range fields | ❌ | ❌ | ✔ | Lucene (Java): `FloatRange` / `DoubleRange` and their DocValues fields. |
| IP-address field | ❌ | ❌ | ✔ | Lucene (Java): `InetAddressPoint`. |
| GeoPointField | ✔   `GeoPointField` | ◐ | ✔ | Lat/lon encoded as `long`; Lucene.NET provides spatial APIs rather than modern `LatLonPoint`. |
| BinaryField | ✔   `BinaryField` | ✔ | ✔ | Arbitrary byte array storage with binary DocValues |
| Field stored vs indexed toggle | ✔   `stored:` param on field constructors | ✔ | ✔ | |
| Per-field analyser assignment | ✔   `IndexWriterConfig.FieldAnalysers` | ✔ | ✔ | |
| Attribute-based document mapping | ✔   `LeanDocumentMap<T>` / `[LeanDocument]` | ❌ | ❌ | Source-generated, reflection-free typed mapping with compile-time schema validation. |
| JSON-to-document mapping | ✔   `JsonDocumentMapper` | ❌ | ❌ | Maps `JsonElement` trees to `LeanDocument` using prefix-path fields and multi-valued arrays. |
| Term vectors (with offsets) | ✔   `StoreTermVectors` / `TermVectorsWriter` | ✔ | ✔ | |
| Per-field index-time boosting | ✔   `IField.Boost` / field constructor `boost:` | ✔ | ◐ | Lucene.NET retains `Field.Boost`; current Java Lucene recommends similarity or DocValues-based alternatives. |
| Term vector positions + payloads | ✔   `TermVectorEntry.Positions` / `TermVectorEntry.Payloads` | ✔ | ✔ | Preserved through flush, merge, migration, and reading. |
| Field name constraints | ✔   `FieldNameValidator` | ❌ | ❌ | Not in Lucene |

---

## Indexing

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| `IndexWriter` | ✔   `IndexWriter` | ✔ | ✔ | |
| RAM buffer flush | ✔   `RamBufferSizeMB` / `MaxBufferedDocs` | ✔ | ✔ | |
| Backpressure (`MaxQueuedDocs`) | ✔   `IndexWriterConfig.MaxQueuedDocs` | ❌ | ❌ | Blocks `AddDocument` when the pending queue is full. |
| Segment backpressure | ✔   `IndexWriterConfig.MergeThrottleSegments` | ❌ | ❌ | Blocks writes until merges reduce the segment count. |
| Commit / Rollback | ✔   `writer.Commit()` | ✔ | ✔ | |
| Durable commits (fsync) | ✔   `IndexWriterConfig.DurableCommits` | ◐ | ◐ | Explicit fsync-before-rename guard with graceful fallback. |
| Atomic document add | ✔   `writer.AddDocument()` | ✔ | ✔ | |
| Atomic update (delete-then-add) | ✔   `writer.UpdateDocument()` | ✔ | ✔ | |
| Delete by query | ✔   `writer.DeleteDocuments()` | ✔ | ✔ | |
| Block-join indexing (nested docs) | ✔   `writer.AddDocumentBlock()` | ✔ | ✔ | |
| Index sort at write time | ✔   `IndexSort` / `IndexWriterConfig.IndexSort` | ❌ | ✔ | Supports numeric and string DocValues field sorts; Lucene.NET 4.8 has no native index sorting. |
| Schema validation | ✔   `IndexSchema` / `SchemaValidationException` | ❌ | ❌ | Enforces field types and required fields during `AddDocument`. |
| Concurrent indexing | ✔   `IndexWriter.Concurrent.*` | ✔ | ✔ | Multi-threaded doc processing |
| Segment merges (background) | ✔   `SegmentMerger` | ✔ | ✔ | |
| Tiered merge policy | ✔   `TieredMergePolicy` | ✔ | ✔ | Count threshold per size tier |
| Async indexing API | ✔   `AddDocumentAsync` / `AddDocumentsAsync` | ❌ | ❌ | LeanCorpus-native `ValueTask` indexing API; Lucene writers are synchronous. |
| `IAsyncEnumerable` bulk ingestion | ✔   `AddDocumentsAsync(IAsyncEnumerable<>, batchSize)` | ❌ | ❌ | LeanCorpus-native streamed, bounded-batch ingestion. |
| HNSW graph build config | ✔   `IndexWriterConfig.HnswBuildConfig` / `HnswSeed` / `BuildHnswOnFlush` | ❌ | ◐ | Per-index build configuration; Java Lucene exposes comparable construction parameters through vector formats. |
| Vector normalisation at index time | ✔   `IndexWriterConfig.NormaliseVectors` | ❌ | ❌ | L2-normalises vectors so dot product equals cosine similarity. |
| Soft deletes | ✔   `IndexWriter.SoftDeleteDocuments(TermQuery)` | ❌ | ✔ | Query form is currently term-query based; Lucene.NET 4.8 predates soft deletes. |
| Sequence numbers / update-by-query | ✔   `NextSequenceNumber` / `TrackSequenceNumbers` / `UpdateDocuments(Query, LeanDocument)` | ◐ | ✔ | Sequence metadata is persisted and merged; update-by-query replaces matching documents atomically. |
| `AddIndexes` (merge from directory) | ✔   `IndexWriter.AddIndexes(MMapDirectory)` | ✔ | ✔ | |
| Payloads on postings | ✔   `StorePayloads` / `PostingsEnum.GetPayload()` | ✔ | ✔ | Written, merged, migrated, and read by the postings codec. |
| Per-field index options | ✔   `FieldIndexOptions` | ✔ | ✔ | Supports documents, frequencies, positions, and offsets. |
| Per-document index-time boosting | ❌ | ❌ | ❌ | LeanCorpus supports per-field index-time boosts and query-time boosts, not a document-wide index boost. |
| LogByteSizeMergePolicy | ✔   `LogByteSizeMergePolicy` | ✔ | ✔ | |
| NoMergePolicy | ✔   `NoMergePolicy` | ✔ | ✔ | |
| TwoPhaseCommit (IndexWriter) | ✔   `PrepareCommit()` / `Commit()` / `Rollback()` | ✔ | ✔ | Prepared commits remain invisible until committed and can be rolled back. |

---

## Query Types

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| TermQuery | ✔   `TermQuery` | ✔ | ✔ | |
| BooleanQuery | ✔   `BooleanQuery` | ✔ | ✔ | Must / Should / MustNot |
| PhraseQuery (with slop) | ✔   `PhraseQuery` | ✔ | ✔ | |
| PrefixQuery | ✔   `PrefixQuery` | ✔ | ✔ | |
| WildcardQuery | ✔   `WildcardQuery` | ✔ | ✔ | `?` and `*` |
| FuzzyQuery | ✔   `FuzzyQuery` | ✔ | ✔ | Levenshtein |
| RangeQuery / TermRangeQuery | ✔   `RangeQuery` / `TermRangeQuery` | ✔ | ✔ | |
| NumericRangeQuery (BKD-backed) | ✔   `RangeQuery` on `NumericField` | ✔ | ✔ | |
| Int64RangeQuery | ✔   `Int64RangeQuery` | ◐ | ✔ | Dedicated signed 64-bit inclusive range query. |
| Int64PointInSetQuery | ✔   `Int64PointInSetQuery` | ◐ | ✔ | Dedicated signed 64-bit point-set query. |
| RegexpQuery | ◐   `RegexpQuery` | ✔ | ✔ | Enumerates terms through the FST but matches with `System.Text.RegularExpressions`, rather than Lucene's automaton implementation. |
| ConstantScoreQuery | ✔   `ConstantScoreQuery` | ✔ | ✔ | |
| DisjunctionMaxQuery | ✔   `DisjunctionMaxQuery` | ✔ | ✔ | |
| FunctionScoreQuery | ✔   `FunctionScoreQuery` | ◐ | ✔ | Lucene.NET provides comparable `FunctionQuery`, `ValueSource`, and custom-scoring APIs. |
| MoreLikeThisQuery | ✔   `MoreLikeThisQuery` | ✔ | ✔ | |
| VectorQuery / kNN | ✔   `VectorQuery` | ❌ | ✔ | Lucene (Java): `KnnFloatVectorQuery`; Lucene.NET 4.8 has no vector API. |
| SpanNearQuery | ✔   `SpanNearQuery` | ✔ | ✔ | |
| SpanOrQuery | ✔   `SpanOrQuery` | ✔ | ✔ | |
| SpanNotQuery | ✔   `SpanNotQuery` | ✔ | ✔ | |
| SpanTermQuery | ✔   `SpanTermQuery` | ✔ | ✔ | |
| BlockJoinQuery | ✔   `BlockJoinQuery` | ✔ | ✔ | Single-level parent/child |
| GeoBoundingBoxQuery | ✔   `GeoBoundingBoxQuery` | ✔ | ✔ | |
| GeoDistanceQuery | ✔   `GeoDistanceQuery` | ✔ | ✔ | |
| RrfQuery (Reciprocal Rank Fusion) | ✔   `RrfQuery` | ❌ | ◐ | Java Lucene 10.3.1 provides result-level fusion through `TopDocs.rrf()`, not a query type. |
| MultiPhraseQuery | ✔   `MultiPhraseQuery` | ✔ | ✔ | |
| IntervalsQuery family | ✔   `IntervalsQuery` | ❌ | ✔ | Lucene (Java): `Intervals`; Lucene.NET 4.8 has no intervals API. |
| PointInSetQuery | ✔   `PointInSetQuery` | ✔ | ✔ | |
| MatchAllDocsQuery | ✔   `MatchAllDocsQuery` | ✔ | ✔ | |
| MatchNoDocsQuery | ✔   `MatchNoDocsQuery` | ✔ | ✔ | |
| FieldExistsQuery | ✔   `FieldExistsQuery` | ❌ | ✔ | Lucene (Java): `FieldExistsQuery`; Lucene.NET 4.8 has no equivalent query. |
| CombinedFieldsQuery (BM25F) | ✔   `CombinedFieldsQuery` | ❌ | ✔ | Lucene (Java): `CombinedFieldsQuery`; Lucene.NET 4.8 predates it. |
| TermInSetQuery | ✔   `TermInSetQuery` | ✔ | ✔ | |
| Multi-level BlockJoinQuery | ❌ | ✔ | ✔ | Backlog |
| ToParentBlockJoinSortField | ❌ | ✔ | ✔ | Backlog |
| Join queries (term-based join) | ❌ | ✔ | ✔ | Backlog |
| Function queries / DoubleValuesSource | ❌ | ◐ | ✔ | Lucene.NET has its older `ValueSource` function-query surface but not Java Lucene's current `DoubleValuesSource` API. |
| BoostQuery (wrapper) | ✔   `Query.Boost` / `QueryExtensions.WithBoost()` | ✔ | ✔ | LeanCorpus uses a base-query property rather than a wrapper type. |
| TermsQuery / TermInSetQuery (byte-ref variant) | ❌ | ✔ | ✔ | Backlog |
| MonitorQuery / Percolator | ❌ | ✔ | ✔ | Backlog |
| MemoryIndex (single-doc in-memory) | ❌ | ✔ | ✔ | Backlog |
| QueryRescorer | ❌ | ✔ | ✔ | Backlog |
| SearchAfter (pagination) | ❌ | ✔ | ✔ | Backlog |
| Count-only search | ✔   `IndexSearcher.Count()` / `CountCollector` | ✔ | ✔ | |
| SpanFirstQuery | ❌ | ✔ | ✔ | Backlog |
| SpanContainingQuery / SpanWithinQuery | ❌ | ✔ | ✔ | Backlog |
| SpanMultiTermQueryWrapper | ❌ | ✔ | ✔ | Backlog |
| SpanFieldMaskingQuery | ❌ | ✔ | ✔ | Backlog |
| Byte-vector kNN | ❌ | ❌ | ✔ | Lucene (Java): `KnnByteVectorField` / `KnnByteVectorQuery`. |
| Vector similarity-threshold query | ❌ | ❌ | ✔ | Lucene (Java): `FloatVectorSimilarityQuery`. |

---

## Query Parsing

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Lucene classic query parser | ✔   `QueryParser` | ✔ | ✔ | `field:term`, phrases, proximity, fuzzy, prefix, boost |
| Programmatic query builder | ✔   `BooleanQueryBuilder` | ✔ | ✔ | |
| Query extensions / helpers | ✔   `QueryExtensions` | ✔ | ✔ | |
| Typed LINQ query provider | ✔   `LeanQueryable<T>` / `LeanQueryProvider<T>` / `LeanExpressionVisitor` | ❌ | ❌ | Translates strongly typed LINQ expressions through source-generated document mappings. |
| `+`/`-` required/excluded syntax | ✔   `QueryParser` | ✔ | ✔ | |
| Range syntax `[a TO b]` | ✔   `QueryParser` | ✔ | ✔ | |
| Grouping `(...)` | ✔   `QueryParser` | ✔ | ✔ | |
| Boost `^N` | ✔   `QueryParser` | ✔ | ✔ | |
| Lenient parsing mode | ✔   `QueryParser` | ✔ | ✔ | |
| Full grammar error positions | ❌ | ✔ | ✔ | Backlog |
| Standard query parser (SQP) | ❌ | ✔ | ✔ | Backlog |
| Analysing query parser | ❌ | ✔ | ✔ | `AnalyzingQueryParser` analyses prefix, wildcard, range, and fuzzy terms. |
| Complex phrase query parser | ❌ | ✔ | ✔ | `ComplexPhraseQueryParser`. |
| Surround query parser | ❌ | ✔ | ✔ | `SurroundQueryParser` supports span-oriented query syntax. |
| XML query parser | ❌ | ✔ | ✔ | `CoreParser` / `XmlQueryParser`. |

---

## Scoring / Similarity

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| BM25 | ✔   `Bm25Similarity` / `Bm25Scorer` | ✔ | ✔ | Default |
| TF-IDF | ✔   `TfIdfSimilarity` | ✔ | ✔ | |
| BlockMaxWAND early termination | ✔   `BlockMaxWandScorer` | ◐ | ◐ | Lucene uses BMWAND internally; LeanCorpus exposes the scorer publicly. |
| Score explanations | ✔   `searcher.Explain()` | ✔ | ✔ | TermQuery and VectorQuery explanations |
| Field boosting (query-time boost in parser) | ✔   `^boost` in query parser | ✔ | ✔ | |
| Pluggable similarity | ✔   `ISimilarity` | ✔ | ✔ | |
| LMDirichletSimilarity | ✔   `DirichletSimilarity` | ✔ | ✔ | |
| LMJelinekMercerSimilarity | ✔   `LMJelinekMercerSimilarity` | ✔ | ✔ | |
| LMAbsoluteDiscountingSimilarity | ✔   `LMAbsoluteDiscountingSimilarity` | ❌ | ❌ | Not in classic Lucene similarity set. |
| BM25L / BM25+ | ✔   `Bm25LSimilarity` / `Bm25PlusSimilarity` | ❌ | ❌ | LeanCorpus extensions to the BM25 family, not built-in Lucene similarities. |
| Augmented TF-IDF | ✔   `TfIdfAugmentedSimilarity` | ❌ | ❌ | Augmented term-frequency variant. |
| Double-normalisation TF-IDF | ✔   `TfIdfDoubleNormSimilarity` | ❌ | ❌ | Double-normalisation term-frequency variant. |
| Pivoted TF-IDF | ✔   `TfIdfPivotedSimilarity` | ◐ | ◐ | Pivoted length normalisation; Lucene can compose related scoring models but has no direct equivalent. |

---

## DocValues

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| NumericDocValues | ✔   `NumericDocValues` / `NumericDocValuesReader` | ✔ | ✔ | |
| SortedDocValues | ✔   `SortedDocValues` / `SortedDocValuesReader` | ✔ | ✔ | |
| SortedSetDocValues | ✔   `SortedSetDocValues` / `SortedSetDocValuesReader` | ✔ | ✔ | |
| SortedNumericDocValues | ✔   `SortedNumericDocValues` / `SortedNumericDocValuesReader` | ✔ | ✔ | |
| BinaryDocValues | ✔   `BinaryDocValues` / `BinaryDocValuesReader` | ✔ | ✔ | |
| Cross-segment ordinal mapping | ❌ | ✔ | ✔ | Lucene: `OrdinalMap` for sorted and sorted-set DocValues. |
| Norms | ✔   `NormsReader` / `NormsWriter` | ✔ | ✔ | |
| Field lengths | ✔   `FieldLengthReader` / `FieldLengthWriter` | ✔ | ✔ | |
| DocValues-backed sort fields | ✔   `SortField.Numeric` / `SortField.String` | ✔ | ✔ | Uses DocValues internally |

---

## Storage / Codecs

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| FST term dictionary | ✔   `FSTBuilder` / `FSTReader` | ✔ | ✔ | |
| BKD tree (numeric + geo) | ✔   `BKDTree` / `BKDReader` | ✔ | ✔ | |
| HNSW vector graph | ✔   `HnswGraph` / `HnswGraphBuilder` / `HnswWriter` / `HnswReader` | ❌ | ✔ | Lucene.NET 4.8 has no vector-search API. |
| Block postings | ✔   `BlockPostingsWriter` / `PostingsReader` / `BlockPostingsEnum` | ✔ | ✔ | |
| Stored fields | ✔   `StoredFieldsWriter` / `StoredFieldsReader` | ✔ | ✔ | |
| Term vectors | ✔   `TermVectorsWriter` / `TermVectorsReader` | ✔ | ✔ | |
| Memory-mapped directory | ✔   `MMapDirectory` | ✔ | ✔ | |
| Atomic file writes | ✔   `IndexAtomicFileWriter` | ✔ | ✔ | |
| Pluggable stored-field compression | ✔   `IFieldCompressionCodec` / `CompressionCodecRegistry` | ◐ | ◐ | Module-initialiser registration; Lucene exposes pluggable `Codec` and `StoredFieldsFormat` APIs at codec level. |
| BCL codecs (None, Deflate, Brotli) | ✔   `NoneCompressionCodec` / `DeflateCompressionCodec` / `BrotliCompressionCodec` | ❌ | ❌ | Built-in LeanCorpus stored-field codecs. |
| LZ4 codec (optional package) | ✔   `Rowles.LeanCorpus.Compression.LZ4` | ✔ | ✔ | Optional extension package with zero-change registration; Lucene uses LZ4 within its stored-field formats. |
| Snappy codec (optional package) | ✔   `Rowles.LeanCorpus.Compression.Snappy` | ❌ | ❌ | Optional extension package with zero-change registration. |
| Zstandard codec (optional package) | ✔   `Rowles.LeanCorpus.Compression.Zstandard` | ❌ | ❌ | Optional extension package with zero-change registration. |
| Compound file (.cfs/.cfe) | ❌ | ✔ | ✔ | Backlog |
| SIMD vector ops (AVX-512) | ✔   `SimdIntrinsicsVectorOps` | ❌ | ◐ | Hand-written AVX-512 cosine and dot-product paths through .NET intrinsics; Java Lucene has platform-vectorised implementations but not this .NET API. |
| Roaring bitmap | ✔   `RoaringBitmap` | ❌ | ◐ | Java Lucene exposes `RoaringDocIdSet`, not the same public bitmap abstraction. |
| Per-field stored-field compression selection | ✔   `FieldCompressionPolicy` | ❌ | ❌ | Compression policy is selected per stored field; Lucene stored-field compression is selected at codec or segment level. |
| Postings format variants (Direct, BlockTree) | ❌ | ✔ | ✔ | Backlog |
| Chunked stored-field format | ❌ | ✔ | ✔ | Backlog |
| Vector quantisation (Int8 / BBQ) | ✔   `IndexWriterConfig.VectorQuantisation` / `VectorQuantisation.Int8` / `VectorQuantisation.BBQ` | ❌ | ✔ | Int8 scalar and BBQ binary quantisation are wired through flush, merge, reader, and HNSW search. |
| 4-bit scalar and product quantisation (PQ) | ❌ | ❌ | ✔ | Lucene (Java) vector formats; Backlog. |
| Approximate kNN over filters | ✔   `VectorQuery` filter and `HnswSearchOptions` | ❌ | ✔ | Supports pre-filter and post-filter modes; Lucene.NET 4.8 has no vector-search API. |
| NioFSDirectory equivalent | ❌ | ✔ | ✔ | Backlog |
| Read-only directory wrapper | ❌ | ◐ | ◐ | Backlog; Lucene directories can be opened for reading or wrapped, but there is no matching first-class API. |
| Object-store pluggable directory | ❌ | ❌ | ❌ | Backlog; none of the compared libraries includes a built-in object-store directory. |
| Directory abstraction | ✔   `LeanDirectory` | ✔ | ✔ | Base abstraction for index storage implementations. |
| Codec composition framework | ✔   `ICodec<T>` / `Codec` / `CodecRegistry` | ◐ | ◐ | LeanCorpus CodecKit provides composable binary codecs, framing, checksums, validation, and versioning beyond index-format selection. |
| Codec migration registry | ✔   `CodecMigrationRegistry` / `CodecVersionStep` | ❌ | ❌ | Ordered in-process format-version migrations. |

---

## Index Management

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Near-real-time search | ✔   `SearcherManager` | ✔ | ✔ | |
| Searcher acquire / release (ref-counted) | ✔   `SearcherManager.Acquire()` / `Release()` | ✔ | ✔ | |
| Background refresh loop | ✔   `SearcherManager` | ✔ | ✔ | |
| Refresh failure tracking | ✔   `LastRefreshError` / `ConsecutiveRefreshFailures` | ❌ | ❌ | Structured refresh-error tracking and a failure event. |
| Index deletion policies | ✔   `IIndexDeletionPolicy` / `KeepLatestCommitPolicy` / `KeepLastNCommitsPolicy` | ✔ | ✔ | |
| Snapshot deletion policy | ✔   `IndexWriter.AcquireSnapshot()` / `ReleaseSnapshot()` | ✔ | ✔ | |
| Index recovery | ✔   `IndexRecovery` | ✔ | ✔ | |
| Live docs (deletion bitmap) | ✔   `LiveDocs` | ✔ | ✔ | |
| Format inspection API | ✔   `IndexFormatInspector.Inspect()` | ❌ | ❌ | Structured inventory of codec versions, sidecars, and orphan files. |
| Compatibility check API | ✔   `IndexCompatibility.Check()` | ❌ | ❌ | Programmatic read/write compatibility verdict before opening an index. |
| Compatibility guardrails for open | ✔   `IndexWriterConfig.CompatibilityMode` / `IndexOpenGuard` | ❌ | ❌ | Blocks or warns before opening an incompatible index. |
| Codec migration API | ✔   `IndexCodecMigrator.Plan()` / `Migrate()` | ❌ | ❌ | Dry-run planning, staged migration, rollback, and abandon without full reindexing. |
| Backup / restore with CRC manifest | ✔   `IndexBackup.Backup()` / `Restore()` | ❌ | ❌ | CRC manifest with file roles, lengths, and checksums; Lucene requires snapshot plus file copy. |
| Index validation / checker | ✔   `IndexValidator.Check()` | ✔ | ✔ | |
| Searcher lease | ✔   `SearcherLease` | ❌ | ❌ | Ref-counted searcher handle with a configurable refresh interval. |
| Query result cache | ✔   `QueryCache` | ❌ | ✔ | Thread-safe, generation-keyed LRU cache per `SearcherManager`; Java Lucene provides `LRUQueryCache`. |
| MultiReader (N directories as one) | ❌ | ✔ | ✔ | Backlog |
| ReaderManager | ❌ | ✔ | ✔ | Backlog |
| InfoStream (writer diagnostic logging) | ❌ | ✔ | ✔ | Backlog |
| Live field values | ❌ | ✔ | ✔ | Lucene: `LiveFieldValues` tracks updates not yet visible through a refreshed searcher. |
| TaxonomyReader / TaxonomyWriter | ❌ | ✔ | ✔ | Backlog |
| ForceMerge (optimise) | ✔   `IndexWriter.ForceMerge(int maxSegments)` | ✔ | ✔ | |
| Incremental backup | ❌ | ◐ | ◐ | Backlog. `IndexBackup.Backup()` currently copies every manifest file and does not compare a prior manifest or skip unchanged files; Lucene supplies snapshot and replication primitives rather than this direct API. |

---

## Faceting and Aggregations

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Term facets | ✔   `SearchWithFacets()` → `FacetsCollector` | ✔ | ✔ | |
| Numeric aggregations (min, max, sum, avg, count) | ✔   `SearchWithAggregations()` / `NumericAggregator` / `AggregationRequest` | ◐ | ◐ | Lucene exposes value-source and facet aggregation primitives, not the same request API. |
| Histogram aggregation | ✔   `AggregationType.Histogram` | ❌ | ❌ | Fixed-bucket LeanCorpus aggregation; neither Lucene baseline has a direct histogram aggregation API. |
| Field collapsing / result grouping | ✔   `SearchWithCollapse()` / `CollapseField` / `CollapseMode` | ◐ | ◐ | Single-field deduplication by top score or first occurrence; Lucene grouping is broader but not the same API. |
| Drill-down facets | ❌ | ✔ | ✔ | `DrillDownQuery`; LeanCorpus currently has no facet-filter query surface. |
| Drill-sideways facets | ❌ | ✔ | ✔ | `DrillSideways` computes sideways counts alongside drill-down results. |
| Range facets (numeric + date) | ❌ | ✔ | ✔ | Backlog |
| Hierarchical / taxonomy facets | ❌ | ✔ | ✔ | Backlog |
| Date histogram with calendar rounding | ❌ | ❌ | ❌ | Backlog; this is not a built-in Lucene aggregation. |
| Percentile aggregator (HDR / t-digest) | ❌ | ❌ | ❌ | Backlog; this is not a built-in Lucene aggregation. |
| Cardinality aggregator (HyperLogLog) | ❌ | ❌ | ❌ | Backlog; this is not a built-in Lucene aggregation. |

---

## Search Extensions

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Document classification | ❌ | ✔ | ✔ | Lucene classification modules include k-nearest-neighbour and Naive Bayes classifiers. |
| Diversified top-doc collection | ◐   `SearchWithCollapse()` | ✔ | ✔ | LeanCorpus can collapse on a field but does not expose Lucene's `DiversifiedTopDocsCollector` contract. |
| Numeric expression scoring | ❌ | ✔ | ✔ | Lucene Expressions compiles formulae over scores and numeric values; unrelated to LeanCorpus's LINQ query provider. |

---

## Highlighting

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Standard Highlighter | ✔   `Highlighter` | ✔ | ✔ | |
| Unified Highlighter | ◐   `HybridHighlighter` | ✔ | ✔ | LeanCorpus hybrid strategy rather than Lucene's exact `UnifiedHighlighter` implementation. |
| Postings Highlighter | ✔   `PostingsHighlighter` | ✔ | ◐ | Java Lucene's current unified highlighting supersedes the older standalone postings highlighter. |
| Fast Vector Highlighter | ◐   `TermVectorHighlighter` | ✔ | ✔ | Term-vector-based equivalent rather than Lucene's exact `FastVectorHighlighter`. |
| Offset source selection | ✔   `Highlighter`, `PostingsHighlighter`, `TermVectorHighlighter`, `HybridHighlighter` | ✔ | ✔ | Select the implementation appropriate to available offsets. |

---

## Suggestions / Spell Check

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| DidYouMean spell checker | ✔   `DidYouMeanSuggester` / `SpellIndex` | ✔ | ✔ | |
| Prefix-based suggestion | ◐   `IndexSearcher.Suggest()` | ✔ | ✔ | Built-in FST completion ranked by global document frequency; comparable to Lucene completion suggesters. |
| Analysing suggester | ❌ | ✔ | ✔ | Backlog |
| Fuzzy suggester | ❌ | ✔ | ✔ | Backlog |
| Context suggester | ❌ | ✔ | ✔ | Backlog |
| FreeTextSuggester | ❌ | ✔ | ✔ | Backlog |

---

## Geo / Spatial

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Geo bounding box query | ✔   `GeoBoundingBoxQuery` | ✔ | ✔ | |
| Geo distance query | ✔   `GeoDistanceQuery` | ✔ | ✔ | |
| Geo encoding utilities | ✔   `GeoEncodingUtils` | ✔ | ✔ | |
| LatLonPoint (BKD-backed lat/lon) | ◐   `GeoPointField` + `BKDTree` | ❌ | ✔ | LeanCorpus provides equivalent point indexing under a different field API; Lucene.NET 4.8 predates `LatLonPoint`. |
| Lat/lon shape field and queries | ❌ | ❌ | ✔ | Lucene (Java): `LatLonShape`. |
| Cartesian shapes | ❌ | ❌ | ✔ | Lucene (Java): `XYShape`. |
| Polygon / line string spatial | ❌ | ◐ | ✔ | Lucene.NET offers comparable Spatial4n strategies rather than Java Lucene's BKD shape API. |
| Recursive prefix tree strategies | ❌ | ✔ | ✔ | Backlog |
| BKD-backed geo shapes | ❌ | ❌ | ✔ | Java Lucene: `LatLonShape`; Lucene.NET 4.8 predates the BKD shape API. |
| XYPoint (cartesian) | ❌ | ❌ | ✔ | Backlog |

---

## Diagnostics / Observability

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Metrics collector | ✔   `IMetricsCollector` / `DefaultMetricsCollector` / `MeterMetricsCollector` | ❌ | ❌ | Not in Lucene |
| OpenTelemetry ActivitySource (traces) | ✔   `LeanCorpusActivitySource` | ❌ | ❌ | ActivitySource spans across indexing, search, migration, and backup. |
| Meter instruments (counters, histograms) | ✔   `LeanCorpusMaintenanceMetrics` | ❌ | ❌ | First-class Meter instruments across index maintenance. |
| Slow query log | ✔   `SlowQueryLog` | ❌ | ❌ | Ring buffer of queries exceeding a configurable threshold. |
| Search analytics | ✔   `SearchAnalytics` | ❌ | ❌ | In-process ring buffer of recent search events. |
| Index size report | ✔   `IndexSizeReport` / `IndexSizeCalculator` | ◐ | ◐ | Lucene exposes low-level file and segment information rather than the same report API. |
| Segment stats | ✔   `SegmentStats` / `IndexStats` | ◐ | ◐ | Lucene exposes segment metadata and diagnostic tools rather than the same typed report. |

---

## Per-query Resource Controls

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| Per-query timeout | ✔   `SearchOptions.Timeout` | ✔ | ✔ | Lucene has `TimeLimitingCollector` |
| Per-query memory budget | ✔   `SearchOptions.MaxResultBytes` | ❌ | ❌ | Hard cap on intermediate-result bytes. |
| Per-query cancellation | ✔   `SearchOptions.CancellationToken` | ❌ | ◐ | Cooperative cancellation between segments; Java Lucene has `QueryTimeout`, not cancellation-token semantics. |
| Partial result flag | ✔   `TopDocs.IsPartial` | ❌ | ❌ | Signals incomplete results caused by timeout or budget. |
| Streaming segment-by-segment results | ✔   `searcher.SearchStreaming()` | ❌ | ❌ | Yields `ScoreDoc` results segment by segment for pipelines. |
| Asynchronous streaming search | ✔   `searcher.SearchAsync()` | ❌ | ❌ | `IAsyncEnumerable<ScoreDoc>` with timeout, memory-budget, and cancellation support. |
| Per-segment collector wrapping | ✔   `TopNCollectorWrapper` | ❌ | ❌ | Not in Lucene |

---

## Utilities / Tools

| Feature | In LeanCorpus | In Lucene.NET | In Lucene (Java) | Notes |
|---|---|---|---|---|
| CLI index tool | ✔   `leancorpus-cli.exe` | ✔ | ◐ | Lucene.NET provides `lucene-cli`; Java Lucene provides lower-level command-line tools and Luke. |
| CLI `check` command | ✔   `leancorpus-cli.exe check` | ◐ | ◐ | Comparable index-checking tools exist, but not this command contract. |
| CLI `inspect` command | ✔   `leancorpus-cli.exe inspect` | ◐ | ◐ | Luke provides comparable inspection, but not this structured command contract. |
| CLI `compat` command | ✔   `leancorpus-cli.exe compat` | ❌ | ❌ | LeanCorpus-specific compatibility verdict. |
| CLI `migrate` command | ✔   `leancorpus-cli.exe migrate` | ❌ | ❌ | LeanCorpus-specific staged codec migration. |
| CLI `backup` / `restore` commands | ✔   `leancorpus-cli.exe backup` / `restore` | ❌ | ❌ | LeanCorpus-specific manifest-backed backup and restore. |
| JSON output from CLI | ✔   `--json` flag | ❌ | ❌ | Structured JSON output from every CLI command. |
| Desktop index browser (Luke) | ❌ | ✔ | ✔ | No GUI equivalent planned |
| Native AOT compatibility | ✔   AOT-safe core; `aot-smoke.ps1` | ❌ | ❌ | Trim-safe core with no dynamic code; Lucene.NET is not AOT-compatible. |
| Source-generated JSON metadata | ✔   `System.Text.Json` source generation throughout | ❌ | ❌ | Reflection-free LeanCorpus serialisation metadata. |
| Source-generated document mapping | ✔   `Rowles.LeanCorpus.SourceGen` | ❌ | ❌ | Compile-time attribute-based field-descriptor generation. |

---
