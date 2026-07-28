---
title: Feature comparison
_description: Compare LeanCorpus features with Lucene.NET and Lucene for Java.
---

<link href="https://unpkg.com/tabulator-tables@6.5.0/dist/css/tabulator.min.css" rel="stylesheet">

# Feature comparison

✔ means a direct equivalent is available, ◐ means a broadly comparable capability, and ❌ means no equivalent is available.

Lucene.NET refers to the packaged 4.8 line. Use the column filters to narrow the results, select a heading to sort, or change the grouping below.

<div class="feature-comparison-toolbar">
  <label for="feature-comparison-group">Group by</label>
  <select id="feature-comparison-group" class="form-select form-select-sm">
    <option value="">Nothing</option>
    <option value="category" selected>Category</option>
  </select>
  <span id="feature-comparison-count" aria-live="polite"></span>
</div>

<div id="feature-comparison-table" aria-label="LeanCorpus and Lucene feature comparison"></div>

<style>
  .feature-comparison-toolbar {
    align-items: center;
    display: flex;
    gap: 0.5rem;
    margin: 0.75rem 0;
  }

  .feature-comparison-toolbar select {
    width: auto;
  }

  #feature-comparison-count {
    color: var(--bs-secondary-color);
    margin-left: auto;
  }

  #feature-comparison-table {
    font-size: 0.82rem;
    height: 72vh;
    min-height: 28rem;
    width: 100%;
  }

  #feature-comparison-table .tabulator-header .tabulator-col,
  #feature-comparison-table .tabulator-row .tabulator-cell {
    padding: 0.25rem 0.4rem;
  }

  #feature-comparison-table .tabulator-cell[tabulator-field="notes"] {
    white-space: normal;
  }

  [data-bs-theme="dark"] #feature-comparison-table.tabulator {
    background-color: var(--bs-body-bg);
    border-color: var(--bs-border-color);
    color: var(--bs-body-color);
  }

  [data-bs-theme="dark"] #feature-comparison-table .tabulator-header,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-header .tabulator-col,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-row,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-row-even,
  [data-bs-theme="dark"] #feature-comparison-table .tabulator-group {
    background-color: var(--bs-body-bg);
    border-color: var(--bs-border-color);
    color: var(--bs-body-color);
  }

  [data-bs-theme="dark"] #feature-comparison-table .tabulator-row:hover {
    background-color: var(--bs-tertiary-bg);
  }

  [data-bs-theme="dark"] #feature-comparison-table input {
    background-color: var(--bs-tertiary-bg);
    border-color: var(--bs-border-color);
    color: var(--bs-body-color);
  }
</style>

<script id="feature-comparison-data" type="application/json">
[
  {
    "feature": "4-bit scalar and product quantisation (PQ)",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java) vector formats; Backlog."
  },
  {
    "feature": "AccentFoldingFilter (ASCIIFoldingFilter)",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔   AccentFoldingFilter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "AddIndexes (merge from directory)",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriter.AddIndexes(MMapDirectory)",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Analysing query parser",
    "category": "Query.Parsing",
    "leancorpus": "✔   AnalysingQueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Analyses literal portions of prefix and wildcard terms."
  },
  {
    "feature": "Analysing suggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Approximate kNN over filters",
    "category": "Storage",
    "leancorpus": "✔   VectorQuery filter and HnswSearchOptions",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Supports pre-filter and post-filter modes; Lucene.NET 4.8 has no vector-search API."
  },
  {
    "feature": "Arabic Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   ArabicStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "Async indexing API",
    "category": "Indexing",
    "leancorpus": "✔   AddDocumentAsync / AddDocumentsAsync",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-native ValueTask indexing API; Lucene writers are synchronous."
  },
  {
    "feature": "Asynchronous streaming search",
    "category": "Query.Controls",
    "leancorpus": "✔   searcher.SearchAsync()",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "IAsyncEnumerable<ScoreDoc> with timeout, memory-budget, and cancellation support."
  },
  {
    "feature": "Atomic document add",
    "category": "Indexing",
    "leancorpus": "✔   writer.AddDocument()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Atomic file writes",
    "category": "Storage",
    "leancorpus": "✔   IndexAtomicFileWriter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Atomic update (delete-then-add)",
    "category": "Indexing",
    "leancorpus": "✔   writer.UpdateDocument()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Attribute-based document mapping",
    "category": "Document",
    "leancorpus": "✔   LeanDocumentMap<T> / [LeanDocument]",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Source-generated, reflection-free typed mapping with compile-time schema validation."
  },
  {
    "feature": "Augmented TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔   TfIdfAugmentedSimilarity",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Augmented term-frequency variant."
  },
  {
    "feature": "Background refresh loop",
    "category": "Indexing.Management",
    "leancorpus": "✔   SearcherManager",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Backpressure (MaxQueuedDocs)",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriterConfig.MaxQueuedDocs",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Blocks AddDocument when the pending queue is full."
  },
  {
    "feature": "Backup & restore with CRC manifest",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexBackup.Backup() / Restore()",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "CRC manifest with file roles, lengths, and checksums; Lucene requires snapshot plus file copy."
  },
  {
    "feature": "BCL codecs (None, Deflate, Brotli)",
    "category": "Storage",
    "leancorpus": "✔   NoneCompressionCodec / DeflateCompressionCodec / BrotliCompressionCodec",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Built-in LeanCorpus stored-field codecs."
  },
  {
    "feature": "BinaryDocValues",
    "category": "DocValues",
    "leancorpus": "✔   BinaryDocValues / BinaryDocValuesReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "BinaryField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Arbitrary byte array storage with binary DocValues"
  },
  {
    "feature": "BKD tree (numeric + geo)",
    "category": "Storage",
    "leancorpus": "✔   BKDTree / BKDReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "BKD-backed geo shapes",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Java Lucene: LatLonShape; Lucene.NET 4.8 predates the BKD shape API."
  },
  {
    "feature": "Block postings",
    "category": "Storage",
    "leancorpus": "✔   BlockPostingsWriter / PostingsReader / BlockPostingsEnum",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Block-join indexing (nested docs)",
    "category": "Indexing",
    "leancorpus": "✔   writer.AddDocumentBlock()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "BlockJoinQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Single-level parent/child"
  },
  {
    "feature": "BlockMaxWAND early termination",
    "category": "Scoring",
    "leancorpus": "✔   BlockMaxWandScorer",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene uses BMWAND internally; LeanCorpus exposes the scorer publicly."
  },
  {
    "feature": "BM25",
    "category": "Scoring",
    "leancorpus": "✔   Bm25Similarity / Bm25Scorer",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Default"
  },
  {
    "feature": "BM25L & BM25+",
    "category": "Scoring",
    "leancorpus": "✔   Bm25LSimilarity / Bm25PlusSimilarity",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus extensions to the BM25 family, not built-in Lucene similarities."
  },
  {
    "feature": "BooleanQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Must / Should / MustNot"
  },
  {
    "feature": "Boost",
    "category": "Query.Parsing",
    "leancorpus": "✔   QueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "BoostQuery (wrapper)",
    "category": "Query.Types",
    "leancorpus": "✔   Query.Boost / QueryExtensions.WithBoost()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LeanCorpus uses a base-query property rather than a wrapper type."
  },
  {
    "feature": "Byte-vector field",
    "category": "Document",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): KnnByteVectorField."
  },
  {
    "feature": "Byte-vector kNN",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): KnnByteVectorField / KnnByteVectorQuery."
  },
  {
    "feature": "CachingTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "CapitialisationFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Applies normal capitalisation rules to tokens."
  },
  {
    "feature": "Cardinality aggregator (HyperLogLog)",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Cartesian shapes",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): XYShape."
  },
  {
    "feature": "Char-level filters (before tokenisation)",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ IndexWriterConfig.CharFilters",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Ordered character-filter pipeline before analyser tokenisation."
  },
  {
    "feature": "Chinese lexicon tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ ChineseLexiconTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Greedy longest-match segmentation with unigram fallback"
  },
  {
    "feature": "Chinese Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "◐   ChineseStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "Identity no-op adapter; Chinese word segmentation is handled by ChineseLexiconTokeniser"
  },
  {
    "feature": "Chunked stored-field format",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "CJK bigram tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ CJKBigramTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: CJKBigramTokenizer"
  },
  {
    "feature": "Classic tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: legacy ClassicTokenizer"
  },
  {
    "feature": "ClassicFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "CLI backup & restore commands",
    "category": "Tools",
    "leancorpus": "✔   leancorpus-cli.exe backup / restore",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-specific manifest-backed backup and restore."
  },
  {
    "feature": "CLI check command",
    "category": "Tools",
    "leancorpus": "✔   leancorpus-cli.exe check",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Comparable index-checking tools exist, but not this command contract."
  },
  {
    "feature": "CLI compat command",
    "category": "Tools",
    "leancorpus": "✔   leancorpus-cli.exe compat",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-specific compatibility verdict."
  },
  {
    "feature": "CLI index tool",
    "category": "Tools",
    "leancorpus": "✔   leancorpus-cli.exe",
    "luceneNet": "✔",
    "luceneJava": "◐",
    "notes": "Lucene.NET provides lucene-cli; Java Lucene provides lower-level command-line tools and Luke."
  },
  {
    "feature": "CLI inspect command",
    "category": "Tools",
    "leancorpus": "✔   leancorpus-cli.exe inspect",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Luke provides comparable inspection, but not this structured command contract."
  },
  {
    "feature": "CLI migrate command",
    "category": "Tools",
    "leancorpus": "✔   leancorpus-cli.exe migrate",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-specific staged codec migration."
  },
  {
    "feature": "Codec composition framework",
    "category": "Storage",
    "leancorpus": "✔   ICodec<T> / Codec / CodecRegistry",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "LeanCorpus CodecKit provides composable binary codecs, framing, checksums, validation, and versioning beyond index-format selection."
  },
  {
    "feature": "Codec migration API",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexCodecMigrator.Plan() / Migrate()",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Dry-run planning, staged migration, rollback, and abandon without full reindexing."
  },
  {
    "feature": "Codec migration registry",
    "category": "Storage",
    "leancorpus": "✔   CodecMigrationRegistry / CodecVersionStep",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Ordered in-process format-version migrations."
  },
  {
    "feature": "CodepointCountFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Removes tokens whose codepoint count falls outside a configured range."
  },
  {
    "feature": "CollationKey analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene: CollationKeyAnalyzer; converts tokens to binary CollationKeys for locale-aware range and sort."
  },
  {
    "feature": "CombinedFieldsQuery (BM25F)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): CombinedFieldsQuery; Lucene.NET 4.8 predates it."
  },
  {
    "feature": "Commit and Rollback",
    "category": "Indexing",
    "leancorpus": "✔   writer.Commit()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "CommonGramsFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Compatibility check API",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexCompatibility.Check()",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Programmatic read/write compatibility verdict before opening an index."
  },
  {
    "feature": "Compatibility guardrails for open",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexWriterConfig.CompatibilityMode / IndexOpenGuard",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Blocks or warns before opening an incompatible index."
  },
  {
    "feature": "Complex phrase query parser",
    "category": "Query.Parsing",
    "leancorpus": "✔   ComplexPhraseQueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Converts same-field complex phrase clauses to span queries."
  },
  {
    "feature": "Compound file (.cfs & .cfe)",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "ConcatenateGraphFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Joins every incoming token with a separator into one output per graph path."
  },
  {
    "feature": "Concurrent indexing",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriter.Concurrent.*",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Multi-threaded doc processing"
  },
  {
    "feature": "ConditionalTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Enables or disables wrapped filters based on current token attributes."
  },
  {
    "feature": "ConstantScoreQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Context suggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Count-only search",
    "category": "Query.Types",
    "leancorpus": "✔   IndexSearcher.Count() / CountCollector",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Cross-segment ordinal mapping",
    "category": "DocValues",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Cross-segment ordinal mapping"
  },
  {
    "feature": "Custom analyser composition",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ Analyser / AnalyserFactory",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Date histogram with calendar rounding",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": ""
  },
  {
    "feature": "DateRecogniserFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Filters out tokens that cannot be parsed as dates."
  },
  {
    "feature": "DecimalDigitFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Delete by query",
    "category": "Indexing",
    "leancorpus": "✔   writer.DeleteDocuments()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "DelimitedPayloadTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Splits tokens on a delimiter, encoding the suffix as a payload."
  },
  {
    "feature": "DelimitedTermFrequencyTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Parses delimiter-separated term-frequency pairs from token text."
  },
  {
    "feature": "Desktop index browser",
    "category": "Tools",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: Luke"
  },
  {
    "feature": "DictionaryCompoundWordTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Decomposes compound words into subwords using a brute-force dictionary."
  },
  {
    "feature": "DidYouMean spell checker",
    "category": "Suggestions",
    "leancorpus": "✔   DidYouMeanSuggester / SpellIndex",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Directory abstraction",
    "category": "Storage",
    "leancorpus": "✔   LeanDirectory",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Base abstraction for index storage implementations."
  },
  {
    "feature": "DisjunctionMaxQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Diversified top-doc collection",
    "category": "Search Extensions",
    "leancorpus": "◐   SearchWithCollapse()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LeanCorpus can collapse on a field but does not expose Lucene's DiversifiedTopDocsCollector contract."
  },
  {
    "feature": "Document classification",
    "category": "Search Extensions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene classification modules include k-nearest-neighbour and Naive Bayes classifiers."
  },
  {
    "feature": "Document model",
    "category": "Document",
    "leancorpus": "✔   LeanDocument",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "DocValues-backed sort fields",
    "category": "DocValues",
    "leancorpus": "✔   SortField.Numeric / SortField.String",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Uses DocValues internally"
  },
  {
    "feature": "Double-normalisation TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔   TfIdfDoubleNormSimilarity",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Double-normalisation term-frequency variant."
  },
  {
    "feature": "Drill-down facets",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "DrillDownQuery; LeanCorpus currently has no facet-filter query surface."
  },
  {
    "feature": "Drill-sideways facets",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "DrillSideways computes sideways counts alongside drill-down results."
  },
  {
    "feature": "DropIfFlaggedFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Drops tokens whose flags match a configured combination."
  },
  {
    "feature": "Durable commits (fsync)",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriterConfig.DurableCommits",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Explicit fsync-before-rename guard with graceful fallback."
  },
  {
    "feature": "Dutch Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   DutchStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "Edge n-gram tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ EdgeNGramTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: EdgeNGramTokenizer"
  },
  {
    "feature": "ElisionFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "French elision"
  },
  {
    "feature": "English (Porter and Snowball)",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   EnglishStemmer",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Fast Vector Highlighter",
    "category": "Highlighting",
    "leancorpus": "◐   TermVectorHighlighter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Term-vector-based equivalent rather than Lucene's exact FastVectorHighlighter."
  },
  {
    "feature": "Field boosting (query-time boost in parser)",
    "category": "Scoring",
    "leancorpus": "✔   ^boost in query parser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Field collapsing & result grouping",
    "category": "Faceting",
    "leancorpus": "✔   SearchWithCollapse() / CollapseField / CollapseMode",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Single-field deduplication by top score or first occurrence; Lucene grouping is broader but not the same API."
  },
  {
    "feature": "Field lengths",
    "category": "DocValues",
    "leancorpus": "✔   FieldLengthReader / FieldLengthWriter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Field name constraints",
    "category": "Document",
    "leancorpus": "✔   FieldNameValidator",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": ""
  },
  {
    "feature": "Field stored vs indexed toggle",
    "category": "Document",
    "leancorpus": "✔   stored: param on field constructors",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "FieldExistsQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): FieldExistsQuery; Lucene.NET 4.8 has no equivalent query."
  },
  {
    "feature": "FingerprintFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Outputs a single token as the sorted, de-duplicated concatenation of all input tokens."
  },
  {
    "feature": "FixBrokenOffsetsFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Repairs broken token offsets introduced by preceding filters."
  },
  {
    "feature": "float and double range fields",
    "category": "Document",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): FloatRange / DoubleRange and their DocValues fields."
  },
  {
    "feature": "ForceMerge (optimise)",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexWriter.ForceMerge(int maxSegments)",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Format inspection API",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexFormatInspector.Inspect()",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Structured inventory of codec versions, sidecars, and orphan files."
  },
  {
    "feature": "FreeTextSuggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "French Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   FrenchStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "FST term dictionary",
    "category": "Storage",
    "leancorpus": "✔   FSTBuilder / FSTReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Full grammar error positions",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Function queries & DoubleValuesSource",
    "category": "Query.Types",
    "leancorpus": "✔   FunctionQuery / DoubleValuesSource",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Numeric fields, constants, scores and composed arithmetic sources."
  },
  {
    "feature": "FunctionScoreQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Lucene.NET provides comparable FunctionQuery, ValueSource, and custom-scoring APIs."
  },
  {
    "feature": "Fuzzy suggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "FuzzyQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Levenshtein"
  },
  {
    "feature": "Generic stem token filter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔   StemTokenFilter / SnowballStemmer",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: SnowballFilter"
  },
  {
    "feature": "Geo bounding box query",
    "category": "Geo & Spatial",
    "leancorpus": "✔   GeoBoundingBoxQuery",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Geo distance query",
    "category": "Geo & Spatial",
    "leancorpus": "✔   GeoDistanceQuery",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Geo encoding utilities",
    "category": "Geo & Spatial",
    "leancorpus": "✔   GeoEncodingUtils",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "GeoBoundingBoxQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "GeoDistanceQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "GeoPointField",
    "category": "Document",
    "leancorpus": "✔   GeoPointField",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Lat/lon encoded as `long`; Lucene.NET provides spatial APIs rather than modern `LatLonPoint`."
  },
  {
    "feature": "German Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   GermanStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "Grouping",
    "category": "Query.Parsing",
    "leancorpus": "✔   QueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Hierarchical & taxonomy facets",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": ""
  },
  {
    "feature": "Hindi Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog"
  },
  {
    "feature": "Histogram aggregation",
    "category": "Faceting",
    "leancorpus": "✔   AggregationType.Histogram",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Fixed-bucket LeanCorpus aggregation; neither Lucene baseline has a direct histogram aggregation API."
  },
  {
    "feature": "HNSW graph build config",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriterConfig.HnswBuildConfig / HnswSeed / BuildHnswOnFlush",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Per-index build configuration; Java Lucene exposes comparable construction parameters through vector formats."
  },
  {
    "feature": "HNSW vector graph",
    "category": "Storage",
    "leancorpus": "✔   HnswGraph / HnswGraphBuilder / HnswWriter / HnswReader",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene.NET 4.8 has no vector-search API."
  },
  {
    "feature": "HTMLStripCharFilter",
    "category": "Analysis.Character Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "HunspellStemFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔   HunspellStemFilter + HunspellDictionary",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "HyphenatedWordsFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "HyphenationCompoundWordTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Decomposes compound words into subwords using hyphenation grammars."
  },
  {
    "feature": "IAsyncEnumerable bulk ingestion",
    "category": "Indexing",
    "leancorpus": "✔   AddDocumentsAsync(IAsyncEnumerable<>, batchSize)",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-native streamed, bounded-batch ingestion."
  },
  {
    "feature": "ICU analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ IcuAnalyser / IcuTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Unicode segmenter-backed"
  },
  {
    "feature": "ICU tokeniser (Unicode segmenter)",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔   IcuTokeniser / UnicodeTokenisation",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Incremental backup",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Backlog. IndexBackup.Backup() currently copies every manifest file and does not compare a prior manifest or skip unchanged files; Lucene supplies snapshot and replication primitives rather than this direct API."
  },
  {
    "feature": "Index deletion policies",
    "category": "Indexing.Management",
    "leancorpus": "✔   IIndexDeletionPolicy / KeepLatestCommitPolicy / KeepLastNCommitsPolicy",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Index recovery",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexRecovery",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Index size report",
    "category": "Diagnostics",
    "leancorpus": "✔   IndexSizeReport / IndexSizeCalculator",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene exposes low-level file and segment information rather than the same report API."
  },
  {
    "feature": "Index sort at write time",
    "category": "Indexing",
    "leancorpus": "✔   IndexSort / IndexWriterConfig.IndexSort",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Supports numeric and string DocValues field sorts; Lucene.NET 4.8 has no native index sorting."
  },
  {
    "feature": "Index validation & checker",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexValidator.Check()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "IndexWriter",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "InfoStream (writer diagnostic logging)",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Int64Field",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Dedicated signed 64-bit field; Lucene.NET uses its older numeric field APIs."
  },
  {
    "feature": "Int64PointInSetQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Dedicated signed 64-bit point-set query."
  },
  {
    "feature": "Int64RangeQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Dedicated signed 64-bit inclusive range query."
  },
  {
    "feature": "IntervalsQuery family",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): Intervals; Lucene.NET 4.8 has no intervals API."
  },
  {
    "feature": "IP-address field",
    "category": "Document",
    "leancorpus": "✔   InetAddressField / InetAddressRangeQuery / InetAddressPointInSetQuery",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "IPv4 and IPv6 fields with inclusive range and point-in-set queries; addresses are normalised to 16-byte values."
  },
  {
    "feature": "Italian Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   ItalianStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "Japanese morphological tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔   JapaneseTokeniser + lexicons/japanese.jlc",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Dictionary-backed least-cost Viterbi segmentation using the bundled checksummed Japanese lexicon; custom .jlc codec paths are supported."
  },
  {
    "feature": "Japanese Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "◐   JapaneseStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "Identity no-op adapter; Japanese segmentation is handled by JapaneseTokeniser"
  },
  {
    "feature": "Join queries (term-based join)",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog"
  },
  {
    "feature": "JSON output from CLI",
    "category": "Tools",
    "leancorpus": "✔   --json flag",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Structured JSON output from every CLI command."
  },
  {
    "feature": "JSON-to-document mapping",
    "category": "Document",
    "leancorpus": "✔   JsonDocumentMapper",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Maps JsonElement trees to LeanDocument using prefix-path fields and multi-valued arrays."
  },
  {
    "feature": "KeepWordFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Keyword analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ KeywordAnalyser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: KeywordAnalyzer. Single-token passthrough."
  },
  {
    "feature": "Keyword tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ KeywordTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: KeywordTokenizer"
  },
  {
    "feature": "KeywordMarkerFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "KeywordRepeatFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Emits each token twice: once as keyword and once as non-keyword."
  },
  {
    "feature": "Korean Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "◐   KoreanStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "Identity no-op adapter; Korean uses CJKBigramTokeniser with word tokenisation"
  },
  {
    "feature": "KStem (English)",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   KStemmer + KStemLexicon",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Krovetz stemmer"
  },
  {
    "feature": "Language analysers",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ LanguageAnalyser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene language-specific Analyzer implementations."
  },
  {
    "feature": "Lat lon shape field and queries",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): LatLonShape."
  },
  {
    "feature": "LatLonPoint (BKD-backed lat lon)",
    "category": "Geo & Spatial",
    "leancorpus": "◐   GeoPointField + BKDTree",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "LeanCorpus provides equivalent point indexing under a different field API; Lucene.NET 4.8 predates LatLonPoint."
  },
  {
    "feature": "LengthFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Lenient parsing mode",
    "category": "Query.Parsing",
    "leancorpus": "✔   QueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Letter tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ LetterTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: LetterTokenizer"
  },
  {
    "feature": "Light English (minimal)",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   LightEnglishStemmer",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Krovetz-inspired light"
  },
  {
    "feature": "LimitTokenCountFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "LimitTokenOffsetFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Stops the stream when a token's start offset exceeds a configured limit."
  },
  {
    "feature": "LimitTokenPositionFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Limits emitted tokens to those whose position does not exceed a configured limit."
  },
  {
    "feature": "Live docs (deletion bitmap)",
    "category": "Indexing.Management",
    "leancorpus": "✔   LiveDocs",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Live field values",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: LiveFieldValues tracks updates not yet visible through a refreshed searcher."
  },
  {
    "feature": "LMAbsoluteDiscountingSimilarity",
    "category": "Scoring",
    "leancorpus": "✔   LMAbsoluteDiscountingSimilarity",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": ""
  },
  {
    "feature": "LMDirichletSimilarity",
    "category": "Scoring",
    "leancorpus": "✔   DirichletSimilarity",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "LMJelinekMercerSimilarity",
    "category": "Scoring",
    "leancorpus": "✔   LMJelinekMercerSimilarity",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "LogByteSizeMergePolicy",
    "category": "Indexing",
    "leancorpus": "✔   LogByteSizeMergePolicy",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "LowercaseFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LowercaseFilter"
  },
  {
    "feature": "Lucene classic query parser",
    "category": "Query.Parsing",
    "leancorpus": "✔   QueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "field:term, phrases, proximity, fuzzy, prefix, boost"
  },
  {
    "feature": "LZ4 codec (optional package)",
    "category": "Storage",
    "leancorpus": "✔   Rowles.LeanCorpus.Compression.LZ4",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Optional extension package with zero-change registration; Lucene uses LZ4 within its stored-field formats."
  },
  {
    "feature": "MappingCharFilter",
    "category": "Analysis.Character Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "MatchAllDocsQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "MatchNoDocsQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Memory-mapped directory",
    "category": "Storage",
    "leancorpus": "✔   MMapDirectory",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "MemoryIndex (single-doc in-memory)",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Metaphone phonetic filter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔   MetaphoneFilter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Meter instruments (counters, histograms)",
    "category": "Diagnostics",
    "leancorpus": "✔   LeanCorpusMaintenanceMetrics",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "First-class Meter instruments across index maintenance."
  },
  {
    "feature": "Metrics collector",
    "category": "Diagnostics",
    "leancorpus": "✔   IMetricsCollector / DefaultMetricsCollector / MeterMetricsCollector",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": ""
  },
  {
    "feature": "MinHashFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Generates min-hash tokens for locality-sensitive hashing (LSH)."
  },
  {
    "feature": "MonitorQuery & Percolator",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "MoreLikeThisQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Morfologik dictionary stemmer",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: MorfologikFilter / DictionaryStemmer"
  },
  {
    "feature": "Multi-level BlockJoinQuery",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog"
  },
  {
    "feature": "MultiPhraseQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "MultiReader (N directories as one)",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "N-gram tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ NGramTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: NGramTokenizer"
  },
  {
    "feature": "Native AOT compatibility",
    "category": "Tools",
    "leancorpus": "✔   AOT-safe core; aot-smoke.ps1",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Trim-safe core with no dynamic code; Lucene.NET is not AOT-compatible."
  },
  {
    "feature": "Near-real-time search",
    "category": "Indexing.Management",
    "leancorpus": "✔   SearcherManager",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "NioFSDirectory equivalent",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "NoMergePolicy",
    "category": "Indexing",
    "leancorpus": "✔   NoMergePolicy",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Normalisation filters (Arabic, German, Hindi, Indic)",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog"
  },
  {
    "feature": "Norms",
    "category": "DocValues",
    "leancorpus": "✔   NormsReader / NormsWriter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Numeric aggregations (min, max, sum, avg, count)",
    "category": "Faceting",
    "leancorpus": "✔   SearchWithAggregations() / NumericAggregator / AggregationRequest",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene exposes value-source and facet aggregation primitives, not the same request API."
  },
  {
    "feature": "Numeric expression scoring",
    "category": "Search Extensions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene Expressions compiles formulae over scores and numeric values; unrelated to LeanCorpus's LINQ query provider."
  },
  {
    "feature": "NumericDocValues",
    "category": "DocValues",
    "leancorpus": "✔   NumericDocValues / NumericDocValuesReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "NumericField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "BKD-indexed, sorted-numeric DocValues sidecar"
  },
  {
    "feature": "NumericPayloadTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Encodes a numeric payload value onto each token."
  },
  {
    "feature": "NumericRangeQuery (BKD-backed)",
    "category": "Query.Types",
    "leancorpus": "✔   RangeQuery on NumericField",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Offset source selection",
    "category": "Highlighting",
    "leancorpus": "✔   Highlighter, PostingsHighlighter, TermVectorHighlighter, HybridHighlighter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Select the implementation appropriate to available offsets."
  },
  {
    "feature": "OpenTelemetry ActivitySource (traces)",
    "category": "Diagnostics",
    "leancorpus": "✔   LeanCorpusActivitySource",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "ActivitySource spans across indexing, search, migration, and backup."
  },
  {
    "feature": "Partial result flag",
    "category": "Query.Controls",
    "leancorpus": "✔   TopDocs.IsPartial",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Signals incomplete results caused by timeout or budget."
  },
  {
    "feature": "Path-hierarchy tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔   PathTreeTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: PathHierarchyTokenizer. Has suffix mode, depth payloads, root-aware parsing"
  },
  {
    "feature": "Pattern tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔   PatternTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: PatternTokenizer"
  },
  {
    "feature": "PatternReplaceCharFilter",
    "category": "Analysis.Character Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "PatternReplaceFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Payloads on postings",
    "category": "Indexing",
    "leancorpus": "✔   StorePayloads / PostingsEnum.GetPayload()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Written, merged, migrated, and read by the postings codec."
  },
  {
    "feature": "Per-document index-time boosting",
    "category": "Indexing",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus supports per-field index-time boosts and query-time boosts, not a document-wide index boost."
  },
  {
    "feature": "Per-field analyser assignment",
    "category": "Document",
    "leancorpus": "✔   IndexWriterConfig.FieldAnalysers",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Per-field analysis override",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ IndexWriterConfig.FieldAnalysers",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Per-field index options",
    "category": "Indexing",
    "leancorpus": "✔   FieldIndexOptions",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Supports documents, frequencies, positions, and offsets."
  },
  {
    "feature": "Per-field index-time boosting",
    "category": "Document",
    "leancorpus": "✔   IField.Boost / field constructor boost:",
    "luceneNet": "✔",
    "luceneJava": "◐",
    "notes": "Lucene.NET retains Field.Boost; current Java Lucene recommends similarity or DocValues-based alternatives."
  },
  {
    "feature": "Per-field stored-field compression selection",
    "category": "Storage",
    "leancorpus": "✔   FieldCompressionPolicy",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Compression policy is selected per stored field; Lucene stored-field compression is selected at codec or segment level."
  },
  {
    "feature": "Per-query cancellation",
    "category": "Query.Controls",
    "leancorpus": "✔   SearchOptions.CancellationToken",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Cooperative cancellation between segments; Java Lucene has QueryTimeout, not cancellation-token semantics."
  },
  {
    "feature": "Per-query memory budget",
    "category": "Query.Controls",
    "leancorpus": "✔   SearchOptions.MaxResultBytes",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Hard cap on intermediate-result bytes."
  },
  {
    "feature": "Per-query timeout",
    "category": "Query.Controls",
    "leancorpus": "✔   SearchOptions.Timeout",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene has TimeLimitingCollector"
  },
  {
    "feature": "Per-segment collector wrapping",
    "category": "Query.Controls",
    "leancorpus": "✔   TopNCollectorWrapper",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": ""
  },
  {
    "feature": "Percentile aggregator (HDR & t-digest)",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": ""
  },
  {
    "feature": "Phonetic alternates (Beider-Morse style)",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔   PhoneticAlternatesFilter + PhoneticEncoding",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Emits bounded phonetic expansions at same position"
  },
  {
    "feature": "PhraseQuery (with slop)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Pivoted TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔   TfIdfPivotedSimilarity",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Pivoted length normalisation; Lucene can compose related scoring models but has no direct equivalent."
  },
  {
    "feature": "Pluggable similarity",
    "category": "Scoring",
    "leancorpus": "✔   ISimilarity",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Pluggable stored-field compression",
    "category": "Storage",
    "leancorpus": "✔   IFieldCompressionCodec / CompressionCodecRegistry",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Module-initialiser registration; Lucene exposes pluggable Codec and StoredFieldsFormat APIs at codec level."
  },
  {
    "feature": "PointInSetQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Polygon & line string spatial",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Lucene.NET offers comparable Spatial4n strategies rather than Java Lucene's BKD shape API."
  },
  {
    "feature": "PorterStemFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔ PorterStemmerFilter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Portuguese Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   PortugueseStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "Postings format variants (Direct, BlockTree)",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Postings Highlighter",
    "category": "Highlighting",
    "leancorpus": "✔   PostingsHighlighter",
    "luceneNet": "✔",
    "luceneJava": "◐",
    "notes": "Java Lucene's current unified highlighting supersedes the older standalone postings highlighter."
  },
  {
    "feature": "Prefix-based suggestion",
    "category": "Suggestions",
    "leancorpus": "◐   IndexSearcher.Suggest()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Built-in FST completion ranked by global document frequency; comparable to Lucene completion suggesters."
  },
  {
    "feature": "PrefixQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Programmatic query builder",
    "category": "Query.Parsing",
    "leancorpus": "✔   BooleanQueryBuilder",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "ProtectedTermFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Wraps filters that only apply to tokens not in a protected set."
  },
  {
    "feature": "Query auto-stop-word analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene: QueryAutoStopWordAnalyzer; prevents high-frequency terms from being passed into queries."
  },
  {
    "feature": "Query extensions & helpers",
    "category": "Query.Parsing",
    "leancorpus": "✔   QueryExtensions",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Query result cache",
    "category": "Indexing.Management",
    "leancorpus": "✔   QueryCache",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Thread-safe, generation-keyed LRU cache per SearcherManager; Java Lucene provides LRUQueryCache."
  },
  {
    "feature": "QueryRescorer",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Candidate-only second-pass scoring with configurable score combination."
  },
  {
    "feature": "RAM buffer flush",
    "category": "Indexing",
    "leancorpus": "✔   RamBufferSizeMB / MaxBufferedDocs",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Range facets (numeric + date)",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Range syntax",
    "category": "Query.Parsing",
    "leancorpus": "✔   QueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "RangeQuery & TermRangeQuery",
    "category": "Query.Types",
    "leancorpus": "✔   RangeQuery / TermRangeQuery",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Read-only directory wrapper",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Backlog; Lucene directories can be opened for reading or wrapped, but there is no matching first-class API."
  },
  {
    "feature": "ReaderManager",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Recursive prefix tree strategies",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Refresh failure tracking",
    "category": "Indexing.Management",
    "leancorpus": "✔   LastRefreshError / ConsecutiveRefreshFailures",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Structured refresh-error tracking and a failure event."
  },
  {
    "feature": "RegexpQuery",
    "category": "Query.Types",
    "leancorpus": "◐",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Enumerates terms through the FST but matches with System.Text.RegularExpressions, rather than Lucene's automaton implementation."
  },
  {
    "feature": "RemoveDuplicatesTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Drops tokens at the same position with identical term text."
  },
  {
    "feature": "Required or excluded syntax",
    "category": "Query.Parsing",
    "leancorpus": "✔   QueryParser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "ReverseStringFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Roaring bitmap",
    "category": "Storage",
    "leancorpus": "✔   RoaringBitmap",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Java Lucene exposes RoaringDocIdSet, not the same public bitmap abstraction."
  },
  {
    "feature": "RrfQuery (Reciprocal Rank Fusion)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Java Lucene 10.3.1 provides result-level fusion through TopDocs.rrf(), not a query type."
  },
  {
    "feature": "Russian Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   RussianStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "ScandinavianFoldingFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Folds Scandinavian characters to ASCII (å→a, ø→o, etc.)."
  },
  {
    "feature": "ScandinavianNormalisationFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Normalises interchangeable Scandinavian characters and folded variants."
  },
  {
    "feature": "Schema validation",
    "category": "Indexing",
    "leancorpus": "✔   IndexSchema / SchemaValidationException",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Enforces field types and required fields during AddDocument."
  },
  {
    "feature": "Score explanations",
    "category": "Scoring",
    "leancorpus": "✔   searcher.Explain()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "TermQuery and VectorQuery explanations"
  },
  {
    "feature": "Search analytics",
    "category": "Diagnostics",
    "leancorpus": "✔   SearchAnalytics",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "In-process ring buffer of recent search events."
  },
  {
    "feature": "SearchAfter (pagination)",
    "category": "Query.Types",
    "leancorpus": "✔   IndexSearcher.SearchAfter()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Score/document-ID and multi-field sort cursors."
  },
  {
    "feature": "Searcher acquire & release (ref-counted)",
    "category": "Indexing.Management",
    "leancorpus": "✔   SearcherManager.Acquire() / Release()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Searcher lease",
    "category": "Indexing.Management",
    "leancorpus": "✔   SearcherLease",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Ref-counted searcher handle with a configurable refresh interval."
  },
  {
    "feature": "Segment backpressure",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriterConfig.MergeThrottleSegments",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Blocks writes until merges reduce the segment count."
  },
  {
    "feature": "Segment merges (background)",
    "category": "Indexing",
    "leancorpus": "✔   SegmentMerger",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Segment stats",
    "category": "Diagnostics",
    "leancorpus": "✔   SegmentStats / IndexStats",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene exposes segment metadata and diagnostic tools rather than the same typed report."
  },
  {
    "feature": "Sequence numbers & update-by-query",
    "category": "Indexing",
    "leancorpus": "✔   NextSequenceNumber / TrackSequenceNumbers / UpdateDocuments(Query, LeanDocument)",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Sequence metadata is persisted and merged; update-by-query replaces matching documents atomically."
  },
  {
    "feature": "ShingleFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SIMD vector ops (AVX-512)",
    "category": "Storage",
    "leancorpus": "✔   SimdIntrinsicsVectorOps",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Hand-written AVX-512 cosine and dot-product paths through .NET intrinsics; Java Lucene has platform-vectorised implementations but not this .NET API."
  },
  {
    "feature": "Simple analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ SimpleAnalyser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: SimpleAnalyzer. Letter-only and lowercase."
  },
  {
    "feature": "Slow query log",
    "category": "Diagnostics",
    "leancorpus": "✔   SlowQueryLog",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Ring buffer of queries exceeding a configurable threshold."
  },
  {
    "feature": "Snappy codec (optional package)",
    "category": "Storage",
    "leancorpus": "✔   Rowles.LeanCorpus.Compression.Snappy",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Optional extension package with zero-change registration."
  },
  {
    "feature": "Snapshot deletion policy",
    "category": "Indexing.Management",
    "leancorpus": "✔   IndexWriter.AcquireSnapshot() / ReleaseSnapshot()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Soft deletes",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriter.SoftDeleteDocuments(TermQuery)",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Query form is currently term-query based; Lucene.NET 4.8 predates soft deletes."
  },
  {
    "feature": "SortedDocValues",
    "category": "DocValues",
    "leancorpus": "✔   SortedDocValues / SortedDocValuesReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SortedNumericDocValues",
    "category": "DocValues",
    "leancorpus": "✔   SortedNumericDocValues / SortedNumericDocValuesReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SortedSetDocValues",
    "category": "DocValues",
    "leancorpus": "✔   SortedSetDocValues / SortedSetDocValuesReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Source-generated document mapping",
    "category": "Tools",
    "leancorpus": "✔   Rowles.LeanCorpus.SourceGen",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Compile-time attribute-based field-descriptor generation."
  },
  {
    "feature": "Source-generated JSON metadata",
    "category": "Tools",
    "leancorpus": "✔   System.Text.Json source generation throughout",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Reflection-free LeanCorpus serialisation metadata."
  },
  {
    "feature": "Span-based analysis",
    "category": "Analysis.Analysers",
    "leancorpus": "✔   ISpanTokeniser / ISpanTokenFilter / ISpanTokenSink",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Low-allocation, span-based token processing surface."
  },
  {
    "feature": "SpanContainingQuery & SpanWithinQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SpanFieldMaskingQuery",
    "category": "Query.Types",
    "leancorpus": "✔   FieldMaskingSpanQuery",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SpanFirstQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Spanish Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔   SpanishStemmer",
    "luceneNet": "",
    "luceneJava": "",
    "notes": ""
  },
  {
    "feature": "SpanMultiTermQueryWrapper",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Prefix, wildcard, fuzzy, regex and term-range expansion."
  },
  {
    "feature": "SpanNearQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SpanNotQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SpanOrQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "SpanTermQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Standard analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ StandardAnalyser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: StandardAnalyzer"
  },
  {
    "feature": "Standard Highlighter",
    "category": "Highlighting",
    "leancorpus": "✔   Highlighter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Standard query parser (SQP)",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Standard tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ Tokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: StandardTokenizer"
  },
  {
    "feature": "Stemmed analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ StemmedAnalyser",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Wraps any IStemmer; Lucene composes an Analyzer with stemming filters."
  },
  {
    "feature": "StemmerOverrideFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Overrides stemming with dictionary-based custom stem mappings."
  },
  {
    "feature": "StopFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔ StopWordFilter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Stored fields",
    "category": "Storage",
    "leancorpus": "✔   StoredFieldsWriter / StoredFieldsReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "StoredField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Stored-only, binary DocValues sidecar"
  },
  {
    "feature": "Streaming segment-by-segment results",
    "category": "Query.Controls",
    "leancorpus": "✔   searcher.SearchStreaming()",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Yields ScoreDoc results segment by segment for pipelines."
  },
  {
    "feature": "StringField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Exact match, sorted-set DocValues sidecar"
  },
  {
    "feature": "Surround query parser",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "SurroundQueryParser supports span-oriented query syntax."
  },
  {
    "feature": "SynonymGraphFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔   SynonymGraphFilter + SynonymMap",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "TaxonomyReader & TaxonomyWriter",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "TeeSinkTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Duplicates a token stream so multiple downstream filters can consume it independently."
  },
  {
    "feature": "Term facets",
    "category": "Faceting",
    "leancorpus": "✔   SearchWithFacets() to FacetsCollector",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Term vector positions + payloads",
    "category": "Document",
    "leancorpus": "✔   TermVectorEntry.Positions / TermVectorEntry.Payloads",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Preserved through flush, merge, migration, and reading."
  },
  {
    "feature": "Term vectors (with offsets)",
    "category": "Document",
    "leancorpus": "✔   StoreTermVectors / TermVectorsWriter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Term vectors",
    "category": "Storage",
    "leancorpus": "✔   TermVectorsWriter / TermVectorsReader",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "TermInSetQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "TermQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "TermsQuery & TermInSetQuery (byte-ref variant)",
    "category": "Query.Types",
    "leancorpus": "✔   TermsQuery",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Accepts exact UTF-8 terms and performs byte-oriented FST lookups."
  },
  {
    "feature": "TextField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Tokenised; the two-argument constructor stores by default."
  },
  {
    "feature": "TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔   TfIdfSimilarity",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Thai tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔   ThaiTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: ThaiTokenizer"
  },
  {
    "feature": "Tiered merge policy",
    "category": "Indexing",
    "leancorpus": "✔   TieredMergePolicy",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Count threshold per size tier"
  },
  {
    "feature": "Token budget & truncation policy",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ MaxTojkensPerDocument / TokenBudgetPolicy",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Truncates or throws when a document exceeds its index-time token limit."
  },
  {
    "feature": "Token count analyser wrapper",
    "category": "Analysis.Analysers",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene: LimitTokenCountAnalyzer; wraps an analyser to cap tokens per field during indexing."
  },
  {
    "feature": "TokenOffsetPayloadTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Encodes token start and end offsets as payloads."
  },
  {
    "feature": "ToParentBlockJoinSortField",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog"
  },
  {
    "feature": "TrimFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Trims leading and trailing whitespace from tokens."
  },
  {
    "feature": "TruncateTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Turkish Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog"
  },
  {
    "feature": "TwoPhaseCommit (IndexWriter)",
    "category": "Indexing",
    "leancorpus": "✔   PrepareCommit() / Commit() / Rollback()",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Prepared commits remain invisible until committed and can be rolled back."
  },
  {
    "feature": "Typed LINQ query provider",
    "category": "Query.Parsing",
    "leancorpus": "✔   LeanQueryable<T> / LeanQueryProvider<T> / LeanExpressionVisitor",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Translates strongly typed LINQ expressions through source-generated document mappings."
  },
  {
    "feature": "TypeTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "UAX29 URL & email tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔   Uax29UrlEmailTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: UAX29URLEmailTokenizer"
  },
  {
    "feature": "Unified Highlighter",
    "category": "Highlighting",
    "leancorpus": "◐   HybridHighlighter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LeanCorpus hybrid strategy rather than Lucene's exact UnifiedHighlighter implementation."
  },
  {
    "feature": "UniqueTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Vector normalisation at index time",
    "category": "Indexing",
    "leancorpus": "✔   IndexWriterConfig.NormaliseVectors",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "L2-normalises vectors so dot product equals cosine similarity."
  },
  {
    "feature": "Vector quantisation (Int8 & BBQ)",
    "category": "Storage",
    "leancorpus": "✔   IndexWriterConfig.VectorQuantisation / VectorQuantisation.Int8 / VectorQuantisation.BBQ",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Int8 scalar and BBQ binary quantisation are wired through flush, merge, reader, and HNSW search."
  },
  {
    "feature": "Vector similarity-threshold query",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): FloatVectorSimilarityQuery."
  },
  {
    "feature": "VectorField",
    "category": "Document",
    "leancorpus": "✔ VectorField",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "float[] for HNSW/kNN; Lucene.NET 4.8 has no vector-search API."
  },
  {
    "feature": "VectorQuery & kNN",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): KnnFloatVectorQuery; Lucene.NET 4.8 has no vector API."
  },
  {
    "feature": "Whitespace analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔ WhitespaceAnalyser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: WhitespaceAnalyzer"
  },
  {
    "feature": "Whitespace tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔ WhitespaceTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: WhitespaceTokenizer"
  },
  {
    "feature": "Wikipedia tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔   MediaWikiTokeniser",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: WikipediaTokenizer"
  },
  {
    "feature": "WildcardQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "? and *"
  },
  {
    "feature": "Word2VecSynonymFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Applies single-token synonyms from a Word2Vec trained model."
  },
  {
    "feature": "WordDelimiterGraphFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔ WordDelimiterFilter",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "XML query parser",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "CoreParser / XmlQueryParser."
  },
  {
    "feature": "XYPoint (cartesian)",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": ""
  },
  {
    "feature": "Zstandard codec (optional package)",
    "category": "Storage",
    "leancorpus": "✔   Rowles.LeanCorpus.Compression.Zstandard",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Optional extension package with zero-change registration."
  }
]
</script>
<script src="https://unpkg.com/tabulator-tables@6.5.0/dist/js/tabulator.min.js"></script>
<script>
  (() => {
    const initialiseFeatureComparison = () => {
      const dataElement = document.getElementById("feature-comparison-data");
      const tableElement = document.getElementById("feature-comparison-table");
      if (!dataElement || !tableElement || typeof Tabulator === "undefined") {
        return;
      }

      const data = JSON.parse(dataElement.textContent);
      const countElement = document.getElementById("feature-comparison-count");
      const updateCount = rows => {
        countElement.textContent = rows.length + " of " + data.length + " features";
      };

      const table = new Tabulator(tableElement, {
        data,
        groupBy: "category",
        groupStartOpen: false,
        height: "72vh",
        initialSort: [{ column: "feature", dir: "asc" }],
        layout: "fitDataStretch",
        placeholder: "No matching features",
        columns: [
          { title: "Feature", field: "feature", headerFilter: "input", minWidth: 220, width: 260 },
          { title: "Category", field: "category", headerFilter: "input", minWidth: 160, width: 190 },
          { title: "LeanCorpus", field: "leancorpus", headerFilter: "input", minWidth: 220, width: 280 },
          { title: "Lucene.NET", field: "luceneNet", headerFilter: "input", minWidth: 110, width: 120 },
          { title: "Lucene (Java)", field: "luceneJava", headerFilter: "input", minWidth: 120, width: 130 },
          {
            title: "Notes",
            field: "notes",
            formatter: "textarea",
            headerFilter: "input",
            minWidth: 360,
            variableHeight: true
          }
        ]
      });

      table.on("dataFiltered", (_filters, rows) => updateCount(rows));
      table.on("tableBuilt", () => updateCount(table.getRows("active")));

      document.getElementById("feature-comparison-group").addEventListener("change", event => {
        table.setGroupBy(event.target.value || false);
      });
    };

    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", initialiseFeatureComparison, { once: true });
    } else {
      initialiseFeatureComparison();
    }
  })();
</script>