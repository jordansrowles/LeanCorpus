---
title: Feature comparison
_description: Compare LeanCorpus features with Lucene.NET and Lucene for Java.
_disableAffix: true
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

  #feature-comparison-table .tabulator-row {
    flex-wrap: wrap;
  }

  #feature-comparison-table .feature-name-cell {
    align-items: center;
    display: flex;
    gap: 0.25rem;
  }

  #feature-comparison-table .feature-detail-toggle {
    background: transparent;
    border: 0;
    color: var(--bs-secondary-color);
    cursor: pointer;
    font-size: 0.95rem;
    line-height: 1;
    padding: 0 0.15rem;
  }

  #feature-comparison-table .feature-detail-toggle:hover,
  #feature-comparison-table .feature-detail-toggle:focus-visible {
    color: var(--bs-body-color);
  }

  #feature-comparison-table .feature-detail-panel {
    border-top: 1px solid var(--bs-border-color);
    box-sizing: border-box;
    display: block;
    flex-basis: 100%;
    padding: 0.55rem 0.75rem 0.65rem 2rem;
    white-space: pre-wrap;
  }

  #feature-comparison-table .feature-detail-panel[hidden] {
    display: none;
  }

  #feature-comparison-table .feature-row-expanded {
    background-color: var(--bs-tertiary-bg);
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
    "notes": "Lucene (Java) vector formats; Backlog.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "AccentFoldingFilter (ASCIIFoldingFilter)",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "AccentFoldingFilter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "AddIndexes (merge from directory)",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexWriter.AddIndexes(MMapDirectory)",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Analysing query parser",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Analyses literal portions of prefix and wildcard terms.",
    "details": {
      "LeanCorpus": "AnalysingQueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Analysing suggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Approximate kNN over filters",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Supports pre-filter and post-filter modes; Lucene.NET 4.8 has no vector-search API.",
    "details": {
      "LeanCorpus": "VectorQuery filter and HnswSearchOptions",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Arabic Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "ArabicStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Async indexing API",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-native ValueTask indexing API; Lucene writers are synchronous.",
    "details": {
      "LeanCorpus": "AddDocumentAsync / AddDocumentsAsync",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Asynchronous streaming search",
    "category": "Query.Controls",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "IAsyncEnumerable<ScoreDoc> with timeout, memory-budget, and cancellation support.",
    "details": {
      "LeanCorpus": "searcher.SearchAsync()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Atomic document add",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "writer.AddDocument()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Atomic file writes",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexAtomicFileWriter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Atomic update (delete-then-add)",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "writer.UpdateDocument()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Attribute-based document mapping",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Source-generated, reflection-free typed mapping with compile-time schema validation.",
    "details": {
      "LeanCorpus": "LeanDocumentMap<T> / [LeanDocument]",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Augmented TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Augmented term-frequency variant.",
    "details": {
      "LeanCorpus": "TfIdfAugmentedSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Background refresh loop",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SearcherManager",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Backpressure (MaxQueuedDocs)",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Blocks AddDocument when the pending queue is full.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.MaxQueuedDocs",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Backup & restore with CRC manifest",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "CRC manifest with file roles, lengths, and checksums; Lucene requires snapshot plus file copy.",
    "details": {
      "LeanCorpus": "IndexBackup.Backup() / Restore()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BCL codecs (None, Deflate, Brotli)",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Built-in LeanCorpus stored-field codecs.",
    "details": {
      "LeanCorpus": "NoneCompressionCodec / DeflateCompressionCodec / BrotliCompressionCodec",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BinaryDocValues",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "BinaryDocValues / BinaryDocValuesReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BinaryField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Arbitrary byte array storage with binary DocValues",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BKD tree (numeric + geo)",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "BKDTree / BKDReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BKD-backed geo shapes",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Java Lucene: LatLonShape; Lucene.NET 4.8 predates the BKD shape API.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Block postings",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "BlockPostingsWriter / PostingsReader / BlockPostingsEnum",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Block-join indexing (nested docs)",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "writer.AddDocumentBlock()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BlockJoinQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Single-level parent/child",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BlockMaxWAND early termination",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene uses BMWAND internally; LeanCorpus exposes the scorer publicly.",
    "details": {
      "LeanCorpus": "BlockMaxWandScorer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BM25",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Default",
    "details": {
      "LeanCorpus": "Bm25Similarity / Bm25Scorer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BM25L & BM25+",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus extensions to the BM25 family, not built-in Lucene similarities.",
    "details": {
      "LeanCorpus": "Bm25LSimilarity / Bm25PlusSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BooleanQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Must / Should / MustNot",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Boost",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "QueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "BoostQuery (wrapper)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LeanCorpus uses a base-query property rather than a wrapper type.",
    "details": {
      "LeanCorpus": "Query.Boost / QueryExtensions.WithBoost()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Byte-vector field",
    "category": "Document",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): KnnByteVectorField.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Byte-vector kNN",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): KnnByteVectorField / KnnByteVectorQuery.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CachingTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CapitialisationFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Applies normal capitalisation rules to tokens.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Cardinality aggregator (HyperLogLog)",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Cartesian shapes",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): XYShape.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Char-level filters (before tokenisation)",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Ordered character-filter pipeline before analyser tokenisation.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.CharFilters",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Chinese lexicon tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Greedy longest-match segmentation with unigram fallback",
    "details": {
      "LeanCorpus": "ChineseLexiconTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Chinese Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "◐",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "Identity no-op adapter; Chinese word segmentation is handled by ChineseLexiconTokeniser",
    "details": {
      "LeanCorpus": "ChineseStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Chunked stored-field format",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CJK bigram tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: CJKBigramTokenizer",
    "details": {
      "LeanCorpus": "CJKBigramTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Classic tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: legacy ClassicTokenizer",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ClassicFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CLI backup & restore commands",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-specific manifest-backed backup and restore.",
    "details": {
      "LeanCorpus": "leancorpus-cli.exe backup / restore",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CLI check command",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Comparable index-checking tools exist, but not this command contract.",
    "details": {
      "LeanCorpus": "leancorpus-cli.exe check",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CLI compat command",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-specific compatibility verdict.",
    "details": {
      "LeanCorpus": "leancorpus-cli.exe compat",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CLI index tool",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "◐",
    "notes": "Lucene.NET provides lucene-cli; Java Lucene provides lower-level command-line tools and Luke.",
    "details": {
      "LeanCorpus": "leancorpus-cli.exe",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CLI inspect command",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Luke provides comparable inspection, but not this structured command contract.",
    "details": {
      "LeanCorpus": "leancorpus-cli.exe inspect",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CLI migrate command",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-specific staged codec migration.",
    "details": {
      "LeanCorpus": "leancorpus-cli.exe migrate",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Codec composition framework",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "LeanCorpus CodecKit provides composable binary codecs, framing, checksums, validation, and versioning beyond index-format selection.",
    "details": {
      "LeanCorpus": "ICodec<T> / Codec / CodecRegistry",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Codec migration API",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Dry-run planning, staged migration, rollback, and abandon without full reindexing.",
    "details": {
      "LeanCorpus": "IndexCodecMigrator.Plan() / Migrate()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Codec migration registry",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Ordered in-process format-version migrations.",
    "details": {
      "LeanCorpus": "CodecMigrationRegistry / CodecVersionStep",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CodepointCountFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Removes tokens whose codepoint count falls outside a configured range.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CollationKey analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene: CollationKeyAnalyzer; converts tokens to binary CollationKeys for locale-aware range and sort.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CombinedFieldsQuery (BM25F)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): CombinedFieldsQuery; Lucene.NET 4.8 predates it.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Commit and Rollback",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "writer.Commit()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "CommonGramsFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Compatibility check API",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Programmatic read/write compatibility verdict before opening an index.",
    "details": {
      "LeanCorpus": "IndexCompatibility.Check()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Compatibility guardrails for open",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Blocks or warns before opening an incompatible index.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.CompatibilityMode / IndexOpenGuard",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Complex phrase query parser",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Converts same-field complex phrase clauses to span queries.",
    "details": {
      "LeanCorpus": "ComplexPhraseQueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Compound file (.cfs & .cfe)",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ConcatenateGraphFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Joins every incoming token with a separator into one output per graph path.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Concurrent indexing",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Multi-threaded doc processing",
    "details": {
      "LeanCorpus": "IndexWriter.Concurrent.*",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ConditionalTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Enables or disables wrapped filters based on current token attributes.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ConstantScoreQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Context suggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Count-only search",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexSearcher.Count() / CountCollector",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Cross-segment ordinal mapping",
    "category": "DocValues",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Cross-segment ordinal mapping",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Custom analyser composition",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "Analyser / AnalyserFactory",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Date histogram with calendar rounding",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DateRecogniserFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Filters out tokens that cannot be parsed as dates.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DecimalDigitFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Delete by query",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "writer.DeleteDocuments()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DelimitedPayloadTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Splits tokens on a delimiter, encoding the suffix as a payload.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DelimitedTermFrequencyTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Parses delimiter-separated term-frequency pairs from token text.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Desktop index browser",
    "category": "Tools",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: Luke",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DictionaryCompoundWordTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Decomposes compound words into subwords using a brute-force dictionary.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DidYouMean spell checker",
    "category": "Suggestions",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "DidYouMeanSuggester / SpellIndex",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Directory abstraction",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Base abstraction for index storage implementations.",
    "details": {
      "LeanCorpus": "LeanDirectory",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DisjunctionMaxQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Diversified top-doc collection",
    "category": "Search Extensions",
    "leancorpus": "◐",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LeanCorpus can collapse on a field but does not expose Lucene's DiversifiedTopDocsCollector contract.",
    "details": {
      "LeanCorpus": "SearchWithCollapse()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Document classification",
    "category": "Search Extensions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene classification modules include k-nearest-neighbour and Naive Bayes classifiers.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Document model",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "LeanDocument",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DocValues-backed sort fields",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Uses DocValues internally",
    "details": {
      "LeanCorpus": "SortField.Numeric / SortField.String",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Double-normalisation TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Double-normalisation term-frequency variant.",
    "details": {
      "LeanCorpus": "TfIdfDoubleNormSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Drill-down facets",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "DrillDownQuery; LeanCorpus currently has no facet-filter query surface.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Drill-sideways facets",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "DrillSideways computes sideways counts alongside drill-down results.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "DropIfFlaggedFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Drops tokens whose flags match a configured combination.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Durable commits (fsync)",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Explicit fsync-before-rename guard with graceful fallback.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.DurableCommits",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Dutch Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "DutchStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Edge n-gram tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: EdgeNGramTokenizer",
    "details": {
      "LeanCorpus": "EdgeNGramTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ElisionFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "French elision",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "English (Porter and Snowball)",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "EnglishStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Fast Vector Highlighter",
    "category": "Highlighting",
    "leancorpus": "◐",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Term-vector-based equivalent rather than Lucene's exact FastVectorHighlighter.",
    "details": {
      "LeanCorpus": "TermVectorHighlighter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Field boosting (query-time boost in parser)",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "^boost in query parser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Field collapsing & result grouping",
    "category": "Faceting",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Single-field deduplication by top score or first occurrence; Lucene grouping is broader but not the same API.",
    "details": {
      "LeanCorpus": "SearchWithCollapse() / CollapseField / CollapseMode",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Field lengths",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "FieldLengthReader / FieldLengthWriter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Field name constraints",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "",
    "details": {
      "LeanCorpus": "FieldNameValidator",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Field stored vs indexed toggle",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "stored: param on field constructors",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "FieldExistsQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): FieldExistsQuery; Lucene.NET 4.8 has no equivalent query.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "FingerprintFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Outputs a single token as the sorted, de-duplicated concatenation of all input tokens.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "FixBrokenOffsetsFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Repairs broken token offsets introduced by preceding filters.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "float and double range fields",
    "category": "Document",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): FloatRange / DoubleRange and their DocValues fields.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ForceMerge (optimise)",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexWriter.ForceMerge(int maxSegments)",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Format inspection API",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Structured inventory of codec versions, sidecars, and orphan files.",
    "details": {
      "LeanCorpus": "IndexFormatInspector.Inspect()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "FreeTextSuggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "French Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "FrenchStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "FST term dictionary",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "FSTBuilder / FSTReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Full grammar error positions",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Function queries & DoubleValuesSource",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Numeric fields, constants, scores and composed arithmetic sources.",
    "details": {
      "LeanCorpus": "FunctionQuery / DoubleValuesSource",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "FunctionScoreQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Lucene.NET provides comparable FunctionQuery, ValueSource, and custom-scoring APIs.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Fuzzy suggester",
    "category": "Suggestions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "FuzzyQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Levenshtein",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Generic stem token filter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: SnowballFilter",
    "details": {
      "LeanCorpus": "StemTokenFilter / SnowballStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Geo bounding box query",
    "category": "Geo & Spatial",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "GeoBoundingBoxQuery",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Geo distance query",
    "category": "Geo & Spatial",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "GeoDistanceQuery",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Geo encoding utilities",
    "category": "Geo & Spatial",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "GeoEncodingUtils",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "GeoBoundingBoxQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "GeoDistanceQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "GeoPointField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Lat/lon encoded as `long`; Lucene.NET provides spatial APIs rather than modern `LatLonPoint`.",
    "details": {
      "LeanCorpus": "GeoPointField",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "German Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "GermanStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Grouping",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "QueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Hierarchical & taxonomy facets",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Hindi Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Histogram aggregation",
    "category": "Faceting",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Fixed-bucket LeanCorpus aggregation; neither Lucene baseline has a direct histogram aggregation API.",
    "details": {
      "LeanCorpus": "AggregationType.Histogram",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "HNSW graph build config",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Per-index build configuration; Java Lucene exposes comparable construction parameters through vector formats.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.HnswBuildConfig / HnswSeed / BuildHnswOnFlush",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "HNSW vector graph",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene.NET 4.8 has no vector-search API.",
    "details": {
      "LeanCorpus": "HnswGraph / HnswGraphBuilder / HnswWriter / HnswReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "HTMLStripCharFilter",
    "category": "Analysis.Character Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "HunspellStemFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "HunspellStemFilter + HunspellDictionary",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "HyphenatedWordsFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "HyphenationCompoundWordTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Decomposes compound words into subwords using hyphenation grammars.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "IAsyncEnumerable bulk ingestion",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus-native streamed, bounded-batch ingestion.",
    "details": {
      "LeanCorpus": "AddDocumentsAsync(IAsyncEnumerable<>, batchSize)",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ICU analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Unicode segmenter-backed",
    "details": {
      "LeanCorpus": "IcuAnalyser / IcuTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ICU tokeniser (Unicode segmenter)",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IcuTokeniser / UnicodeTokenisation",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Incremental backup",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Backlog. IndexBackup.Backup() currently copies every manifest file and does not compare a prior manifest or skip unchanged files; Lucene supplies snapshot and replication primitives rather than this direct API.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Index deletion policies",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IIndexDeletionPolicy / KeepLatestCommitPolicy / KeepLastNCommitsPolicy",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Index recovery",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexRecovery",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Index size report",
    "category": "Diagnostics",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene exposes low-level file and segment information rather than the same report API.",
    "details": {
      "LeanCorpus": "IndexSizeReport / IndexSizeCalculator",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Index sort at write time",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Supports numeric and string DocValues field sorts; Lucene.NET 4.8 has no native index sorting.",
    "details": {
      "LeanCorpus": "IndexSort / IndexWriterConfig.IndexSort",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Index validation & checker",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexValidator.Check()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "IndexWriter",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexWriter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "InfoStream (writer diagnostic logging)",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Int64Field",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Dedicated signed 64-bit field; Lucene.NET uses its older numeric field APIs.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Int64PointInSetQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Dedicated signed 64-bit point-set query.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Int64RangeQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Dedicated signed 64-bit inclusive range query.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "IntervalsQuery family",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): Intervals; Lucene.NET 4.8 has no intervals API.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "IP-address field",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "IPv4 and IPv6 fields with inclusive range and point-in-set queries; addresses are normalised to 16-byte values.",
    "details": {
      "LeanCorpus": "InetAddressField / InetAddressRangeQuery / InetAddressPointInSetQuery",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Italian Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "ItalianStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Japanese morphological tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Dictionary-backed least-cost Viterbi segmentation using the bundled checksummed Japanese lexicon; custom .jlc codec paths are supported.",
    "details": {
      "LeanCorpus": "JapaneseTokeniser + lexicons/japanese.jlc",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Japanese Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "◐",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "Identity no-op adapter; Japanese segmentation is handled by JapaneseTokeniser",
    "details": {
      "LeanCorpus": "JapaneseStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Join queries (term-based join)",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "JSON output from CLI",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Structured JSON output from every CLI command.",
    "details": {
      "LeanCorpus": "--json flag",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "JSON-to-document mapping",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Maps JsonElement trees to LeanDocument using prefix-path fields and multi-valued arrays.",
    "details": {
      "LeanCorpus": "JsonDocumentMapper",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "KeepWordFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Keyword analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: KeywordAnalyzer. Single-token passthrough.",
    "details": {
      "LeanCorpus": "KeywordAnalyser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Keyword tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: KeywordTokenizer",
    "details": {
      "LeanCorpus": "KeywordTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "KeywordMarkerFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "KeywordRepeatFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Emits each token twice: once as keyword and once as non-keyword.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Korean Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "◐",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "Identity no-op adapter; Korean uses CJKBigramTokeniser with word tokenisation",
    "details": {
      "LeanCorpus": "KoreanStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "KStem (English)",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Krovetz stemmer",
    "details": {
      "LeanCorpus": "KStemmer + KStemLexicon",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Language analysers",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene language-specific Analyzer implementations.",
    "details": {
      "LeanCorpus": "LanguageAnalyser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Lat lon shape field and queries",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): LatLonShape.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LatLonPoint (BKD-backed lat lon)",
    "category": "Geo & Spatial",
    "leancorpus": "◐",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "LeanCorpus provides equivalent point indexing under a different field API; Lucene.NET 4.8 predates LatLonPoint.",
    "details": {
      "LeanCorpus": "GeoPointField + BKDTree",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LengthFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Lenient parsing mode",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "QueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Letter tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: LetterTokenizer",
    "details": {
      "LeanCorpus": "LetterTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Light English (minimal)",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Krovetz-inspired light",
    "details": {
      "LeanCorpus": "LightEnglishStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LimitTokenCountFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LimitTokenOffsetFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Stops the stream when a token's start offset exceeds a configured limit.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LimitTokenPositionFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Limits emitted tokens to those whose position does not exceed a configured limit.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Live docs (deletion bitmap)",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "LiveDocs",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Live field values",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: LiveFieldValues tracks updates not yet visible through a refreshed searcher.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LMAbsoluteDiscountingSimilarity",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "",
    "details": {
      "LeanCorpus": "LMAbsoluteDiscountingSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LMDirichletSimilarity",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "DirichletSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LMJelinekMercerSimilarity",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "LMJelinekMercerSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LogByteSizeMergePolicy",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "LogByteSizeMergePolicy",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LowercaseFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LowercaseFilter",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Lucene classic query parser",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "field:term, phrases, proximity, fuzzy, prefix, boost",
    "details": {
      "LeanCorpus": "QueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "LZ4 codec (optional package)",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Optional extension package with zero-change registration; Lucene uses LZ4 within its stored-field formats.",
    "details": {
      "LeanCorpus": "Rowles.LeanCorpus.Compression.LZ4",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MappingCharFilter",
    "category": "Analysis.Character Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MatchAllDocsQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MatchNoDocsQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Memory-mapped directory",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "MMapDirectory",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MemoryIndex (single-doc in-memory)",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Metaphone phonetic filter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "MetaphoneFilter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Meter instruments (counters, histograms)",
    "category": "Diagnostics",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "First-class Meter instruments across index maintenance.",
    "details": {
      "LeanCorpus": "LeanCorpusMaintenanceMetrics",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Metrics collector",
    "category": "Diagnostics",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "",
    "details": {
      "LeanCorpus": "IMetricsCollector / DefaultMetricsCollector / MeterMetricsCollector",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MinHashFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Generates min-hash tokens for locality-sensitive hashing (LSH).",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MonitorQuery & Percolator",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MoreLikeThisQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Morfologik dictionary stemmer",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: MorfologikFilter / DictionaryStemmer",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Multi-level BlockJoinQuery",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MultiPhraseQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "MultiReader (N directories as one)",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "N-gram tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: NGramTokenizer",
    "details": {
      "LeanCorpus": "NGramTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Native AOT compatibility",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Trim-safe core with no dynamic code; Lucene.NET is not AOT-compatible.",
    "details": {
      "LeanCorpus": "AOT-safe core; aot-smoke.ps1",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Near-real-time search",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SearcherManager",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "NioFSDirectory equivalent",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "NoMergePolicy",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "NoMergePolicy",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Normalisation filters (Arabic, German, Hindi, Indic)",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Norms",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "NormsReader / NormsWriter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Numeric aggregations (min, max, sum, avg, count)",
    "category": "Faceting",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene exposes value-source and facet aggregation primitives, not the same request API.",
    "details": {
      "LeanCorpus": "SearchWithAggregations() / NumericAggregator / AggregationRequest",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Numeric expression scoring",
    "category": "Search Extensions",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene Expressions compiles formulae over scores and numeric values; unrelated to LeanCorpus's LINQ query provider.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "NumericDocValues",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "NumericDocValues / NumericDocValuesReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "NumericField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "BKD-indexed, sorted-numeric DocValues sidecar",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "NumericPayloadTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Encodes a numeric payload value onto each token.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "NumericRangeQuery (BKD-backed)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "RangeQuery on NumericField",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Offset source selection",
    "category": "Highlighting",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Select the implementation appropriate to available offsets.",
    "details": {
      "LeanCorpus": "Highlighter, PostingsHighlighter, TermVectorHighlighter, HybridHighlighter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "OpenTelemetry ActivitySource (traces)",
    "category": "Diagnostics",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "ActivitySource spans across indexing, search, migration, and backup.",
    "details": {
      "LeanCorpus": "LeanCorpusActivitySource",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Partial result flag",
    "category": "Query.Controls",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Signals incomplete results caused by timeout or budget.",
    "details": {
      "LeanCorpus": "TopDocs.IsPartial",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Path-hierarchy tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: PathHierarchyTokenizer. Has suffix mode, depth payloads, root-aware parsing",
    "details": {
      "LeanCorpus": "PathTreeTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Pattern tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: PatternTokenizer",
    "details": {
      "LeanCorpus": "PatternTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "PatternReplaceCharFilter",
    "category": "Analysis.Character Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "PatternReplaceFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Payloads on postings",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Written, merged, migrated, and read by the postings codec.",
    "details": {
      "LeanCorpus": "StorePayloads / PostingsEnum.GetPayload()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-document index-time boosting",
    "category": "Indexing",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "LeanCorpus supports per-field index-time boosts and query-time boosts, not a document-wide index boost.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-field analyser assignment",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexWriterConfig.FieldAnalysers",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-field analysis override",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexWriterConfig.FieldAnalysers",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-field index options",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Supports documents, frequencies, positions, and offsets.",
    "details": {
      "LeanCorpus": "FieldIndexOptions",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-field index-time boosting",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "◐",
    "notes": "Lucene.NET retains Field.Boost; current Java Lucene recommends similarity or DocValues-based alternatives.",
    "details": {
      "LeanCorpus": "IField.Boost / field constructor boost:",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-field stored-field compression selection",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Compression policy is selected per stored field; Lucene stored-field compression is selected at codec or segment level.",
    "details": {
      "LeanCorpus": "FieldCompressionPolicy",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-query cancellation",
    "category": "Query.Controls",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Cooperative cancellation between segments; Java Lucene has QueryTimeout, not cancellation-token semantics.",
    "details": {
      "LeanCorpus": "SearchOptions.CancellationToken",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-query memory budget",
    "category": "Query.Controls",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Hard cap on intermediate-result bytes.",
    "details": {
      "LeanCorpus": "SearchOptions.MaxResultBytes",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-query timeout",
    "category": "Query.Controls",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene has TimeLimitingCollector",
    "details": {
      "LeanCorpus": "SearchOptions.Timeout",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Per-segment collector wrapping",
    "category": "Query.Controls",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "",
    "details": {
      "LeanCorpus": "TopNCollectorWrapper",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Percentile aggregator (HDR & t-digest)",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Phonetic alternates (Beider-Morse style)",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Emits bounded phonetic expansions at same position",
    "details": {
      "LeanCorpus": "PhoneticAlternatesFilter + PhoneticEncoding",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "PhraseQuery (with slop)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Pivoted TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Pivoted length normalisation; Lucene can compose related scoring models but has no direct equivalent.",
    "details": {
      "LeanCorpus": "TfIdfPivotedSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Pluggable similarity",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "ISimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Pluggable stored-field compression",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Module-initialiser registration; Lucene exposes pluggable Codec and StoredFieldsFormat APIs at codec level.",
    "details": {
      "LeanCorpus": "IFieldCompressionCodec / CompressionCodecRegistry",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "PointInSetQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Polygon & line string spatial",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Lucene.NET offers comparable Spatial4n strategies rather than Java Lucene's BKD shape API.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "PorterStemFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "PorterStemmerFilter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Portuguese Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "PortugueseStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Postings format variants (Direct, BlockTree)",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Postings Highlighter",
    "category": "Highlighting",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "◐",
    "notes": "Java Lucene's current unified highlighting supersedes the older standalone postings highlighter.",
    "details": {
      "LeanCorpus": "PostingsHighlighter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Prefix-based suggestion",
    "category": "Suggestions",
    "leancorpus": "◐",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Built-in FST completion ranked by global document frequency; comparable to Lucene completion suggesters.",
    "details": {
      "LeanCorpus": "IndexSearcher.Suggest()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "PrefixQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Programmatic query builder",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "BooleanQueryBuilder",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ProtectedTermFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Wraps filters that only apply to tokens not in a protected set.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Query auto-stop-word analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene: QueryAutoStopWordAnalyzer; prevents high-frequency terms from being passed into queries.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Query extensions & helpers",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "QueryExtensions",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Query result cache",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Thread-safe, generation-keyed LRU cache per SearcherManager; Java Lucene provides LRUQueryCache.",
    "details": {
      "LeanCorpus": "QueryCache",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "QueryRescorer",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Candidate-only second-pass scoring with configurable score combination.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "RAM buffer flush",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "RamBufferSizeMB / MaxBufferedDocs",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Range facets (numeric + date)",
    "category": "Faceting",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Range syntax",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "QueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "RangeQuery & TermRangeQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "RangeQuery / TermRangeQuery",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Read-only directory wrapper",
    "category": "Storage",
    "leancorpus": "❌",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Backlog; Lucene directories can be opened for reading or wrapped, but there is no matching first-class API.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ReaderManager",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Recursive prefix tree strategies",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Refresh failure tracking",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Structured refresh-error tracking and a failure event.",
    "details": {
      "LeanCorpus": "LastRefreshError / ConsecutiveRefreshFailures",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "RegexpQuery",
    "category": "Query.Types",
    "leancorpus": "◐",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Enumerates terms through the FST but matches with System.Text.RegularExpressions, rather than Lucene's automaton implementation.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "RemoveDuplicatesTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Drops tokens at the same position with identical term text.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Required or excluded syntax",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "QueryParser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ReverseStringFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Roaring bitmap",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Java Lucene exposes RoaringDocIdSet, not the same public bitmap abstraction.",
    "details": {
      "LeanCorpus": "RoaringBitmap",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "RrfQuery (Reciprocal Rank Fusion)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Java Lucene 10.3.1 provides result-level fusion through TopDocs.rrf(), not a query type.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Russian Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "RussianStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ScandinavianFoldingFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Folds Scandinavian characters to ASCII (å→a, ø→o, etc.).",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ScandinavianNormalisationFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Normalises interchangeable Scandinavian characters and folded variants.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Schema validation",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Enforces field types and required fields during AddDocument.",
    "details": {
      "LeanCorpus": "IndexSchema / SchemaValidationException",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Score explanations",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "TermQuery and VectorQuery explanations",
    "details": {
      "LeanCorpus": "searcher.Explain()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Search analytics",
    "category": "Diagnostics",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "In-process ring buffer of recent search events.",
    "details": {
      "LeanCorpus": "SearchAnalytics",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SearchAfter (pagination)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Score/document-ID and multi-field sort cursors.",
    "details": {
      "LeanCorpus": "IndexSearcher.SearchAfter()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Searcher acquire & release (ref-counted)",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SearcherManager.Acquire() / Release()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Searcher lease",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Ref-counted searcher handle with a configurable refresh interval.",
    "details": {
      "LeanCorpus": "SearcherLease",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Segment backpressure",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Blocks writes until merges reduce the segment count.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.MergeThrottleSegments",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Segment merges (background)",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SegmentMerger",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Segment stats",
    "category": "Diagnostics",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Lucene exposes segment metadata and diagnostic tools rather than the same typed report.",
    "details": {
      "LeanCorpus": "SegmentStats / IndexStats",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Sequence numbers & update-by-query",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "✔",
    "notes": "Sequence metadata is persisted and merged; update-by-query replaces matching documents atomically.",
    "details": {
      "LeanCorpus": "NextSequenceNumber / TrackSequenceNumbers / UpdateDocuments(Query, LeanDocument)",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ShingleFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SIMD vector ops (AVX-512)",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "◐",
    "notes": "Hand-written AVX-512 cosine and dot-product paths through .NET intrinsics; Java Lucene has platform-vectorised implementations but not this .NET API.",
    "details": {
      "LeanCorpus": "SimdIntrinsicsVectorOps",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Simple analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: SimpleAnalyzer. Letter-only and lowercase.",
    "details": {
      "LeanCorpus": "SimpleAnalyser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Slow query log",
    "category": "Diagnostics",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Ring buffer of queries exceeding a configurable threshold.",
    "details": {
      "LeanCorpus": "SlowQueryLog",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Snappy codec (optional package)",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Optional extension package with zero-change registration.",
    "details": {
      "LeanCorpus": "Rowles.LeanCorpus.Compression.Snappy",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Snapshot deletion policy",
    "category": "Indexing.Management",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "IndexWriter.AcquireSnapshot() / ReleaseSnapshot()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Soft deletes",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Query form is currently term-query based; Lucene.NET 4.8 predates soft deletes.",
    "details": {
      "LeanCorpus": "IndexWriter.SoftDeleteDocuments(TermQuery)",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SortedDocValues",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SortedDocValues / SortedDocValuesReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SortedNumericDocValues",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SortedNumericDocValues / SortedNumericDocValuesReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SortedSetDocValues",
    "category": "DocValues",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SortedSetDocValues / SortedSetDocValuesReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Source-generated document mapping",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Compile-time attribute-based field-descriptor generation.",
    "details": {
      "LeanCorpus": "Rowles.LeanCorpus.SourceGen",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Source-generated JSON metadata",
    "category": "Tools",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Reflection-free LeanCorpus serialisation metadata.",
    "details": {
      "LeanCorpus": "System.Text.Json source generation throughout",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Span-based analysis",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Low-allocation, span-based token processing surface.",
    "details": {
      "LeanCorpus": "ISpanTokeniser / ISpanTokenFilter / ISpanTokenSink",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanContainingQuery & SpanWithinQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanFieldMaskingQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "FieldMaskingSpanQuery",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanFirstQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Spanish Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "✔",
    "luceneNet": "",
    "luceneJava": "",
    "notes": "",
    "details": {
      "LeanCorpus": "SpanishStemmer",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanMultiTermQueryWrapper",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Prefix, wildcard, fuzzy, regex and term-range expansion.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanNearQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanNotQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanOrQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SpanTermQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Standard analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: StandardAnalyzer",
    "details": {
      "LeanCorpus": "StandardAnalyser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Standard Highlighter",
    "category": "Highlighting",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "Highlighter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Standard query parser (SQP)",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Standard tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: StandardTokenizer",
    "details": {
      "LeanCorpus": "Tokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Stemmed analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "◐",
    "luceneJava": "◐",
    "notes": "Wraps any IStemmer; Lucene composes an Analyzer with stemming filters.",
    "details": {
      "LeanCorpus": "StemmedAnalyser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "StemmerOverrideFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Overrides stemming with dictionary-based custom stem mappings.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "StopFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "StopWordFilter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Stored fields",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "StoredFieldsWriter / StoredFieldsReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "StoredField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Stored-only, binary DocValues sidecar",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Streaming segment-by-segment results",
    "category": "Query.Controls",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Yields ScoreDoc results segment by segment for pipelines.",
    "details": {
      "LeanCorpus": "searcher.SearchStreaming()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "StringField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Exact match, sorted-set DocValues sidecar",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Surround query parser",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "SurroundQueryParser supports span-oriented query syntax.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "SynonymGraphFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SynonymGraphFilter + SynonymMap",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TaxonomyReader & TaxonomyWriter",
    "category": "Indexing.Management",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TeeSinkTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Duplicates a token stream so multiple downstream filters can consume it independently.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Term facets",
    "category": "Faceting",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "SearchWithFacets() to FacetsCollector",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Term vector positions + payloads",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Preserved through flush, merge, migration, and reading.",
    "details": {
      "LeanCorpus": "TermVectorEntry.Positions / TermVectorEntry.Payloads",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Term vectors (with offsets)",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "StoreTermVectors / TermVectorsWriter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Term vectors",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "TermVectorsWriter / TermVectorsReader",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TermInSetQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TermQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TermsQuery & TermInSetQuery (byte-ref variant)",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Accepts exact UTF-8 terms and performs byte-oriented FST lookups.",
    "details": {
      "LeanCorpus": "TermsQuery",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TextField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Tokenised; the two-argument constructor stores by default.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TF-IDF",
    "category": "Scoring",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "TfIdfSimilarity",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Thai tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: ThaiTokenizer",
    "details": {
      "LeanCorpus": "ThaiTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Tiered merge policy",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Count threshold per size tier",
    "details": {
      "LeanCorpus": "TieredMergePolicy",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Token budget & truncation policy",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Truncates or throws when a document exceeds its index-time token limit.",
    "details": {
      "LeanCorpus": "MaxTojkensPerDocument / TokenBudgetPolicy",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Token count analyser wrapper",
    "category": "Analysis.Analysers",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene: LimitTokenCountAnalyzer; wraps an analyser to cap tokens per field during indexing.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TokenOffsetPayloadTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Encodes token start and end offsets as payloads.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "ToParentBlockJoinSortField",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TrimFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Trims leading and trailing whitespace from tokens.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TruncateTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Turkish Stemmer",
    "category": "Analysis.Stemmers",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Backlog",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TwoPhaseCommit (IndexWriter)",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Prepared commits remain invisible until committed and can be rolled back.",
    "details": {
      "LeanCorpus": "PrepareCommit() / Commit() / Rollback()",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Typed LINQ query provider",
    "category": "Query.Parsing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Translates strongly typed LINQ expressions through source-generated document mappings.",
    "details": {
      "LeanCorpus": "LeanQueryable<T> / LeanQueryProvider<T> / LeanExpressionVisitor",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "TypeTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "UAX29 URL & email tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: UAX29URLEmailTokenizer",
    "details": {
      "LeanCorpus": "Uax29UrlEmailTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Unified Highlighter",
    "category": "Highlighting",
    "leancorpus": "◐",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "LeanCorpus hybrid strategy rather than Lucene's exact UnifiedHighlighter implementation.",
    "details": {
      "LeanCorpus": "HybridHighlighter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "UniqueTokenFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Vector normalisation at index time",
    "category": "Indexing",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "L2-normalises vectors so dot product equals cosine similarity.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.NormaliseVectors",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Vector quantisation (Int8 & BBQ)",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Int8 scalar and BBQ binary quantisation are wired through flush, merge, reader, and HNSW search.",
    "details": {
      "LeanCorpus": "IndexWriterConfig.VectorQuantisation / VectorQuantisation.Int8 / VectorQuantisation.BBQ",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Vector similarity-threshold query",
    "category": "Query.Types",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): FloatVectorSimilarityQuery.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "VectorField",
    "category": "Document",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "float[] for HNSW/kNN; Lucene.NET 4.8 has no vector-search API.",
    "details": {
      "LeanCorpus": "VectorField",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "VectorQuery & kNN",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Lucene (Java): KnnFloatVectorQuery; Lucene.NET 4.8 has no vector API.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Whitespace analyser",
    "category": "Analysis.Analysers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: WhitespaceAnalyzer",
    "details": {
      "LeanCorpus": "WhitespaceAnalyser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Whitespace tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: WhitespaceTokenizer",
    "details": {
      "LeanCorpus": "WhitespaceTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Wikipedia tokeniser",
    "category": "Analysis.Tokenisers",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "Lucene: WikipediaTokenizer",
    "details": {
      "LeanCorpus": "MediaWikiTokeniser",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "WildcardQuery",
    "category": "Query.Types",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "? and *",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Word2VecSynonymFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "Applies single-token synonyms from a Word2Vec trained model.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "WordDelimiterGraphFilter",
    "category": "Analysis.Token Filters",
    "leancorpus": "✔",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "WordDelimiterFilter",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "XML query parser",
    "category": "Query.Parsing",
    "leancorpus": "❌",
    "luceneNet": "✔",
    "luceneJava": "✔",
    "notes": "CoreParser / XmlQueryParser.",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "XYPoint (cartesian)",
    "category": "Geo & Spatial",
    "leancorpus": "❌",
    "luceneNet": "❌",
    "luceneJava": "✔",
    "notes": "",
    "details": {
      "LeanCorpus": "",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
  },
  {
    "feature": "Zstandard codec (optional package)",
    "category": "Storage",
    "leancorpus": "✔",
    "luceneNet": "❌",
    "luceneJava": "❌",
    "notes": "Optional extension package with zero-change registration.",
    "details": {
      "LeanCorpus": "Rowles.LeanCorpus.Compression.Zstandard",
      "Lucene.NET": "",
      "Lucene (Java)": ""
    }
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
      const detailColumns = ["LeanCorpus", "Lucene.NET", "Lucene (Java)"];
      const hasDetails = rowData => rowData.notes || detailColumns.some(column => rowData.details[column]);
      const detailText = rowData => [
        rowData.notes,
        ...detailColumns.map(column => rowData.details[column] ? column + ": " + rowData.details[column] : "")
      ].filter(Boolean).join("\n\n");

      const toggleDetails = (row, toggle) => {
        const rowElement = row.getElement();
        const panel = rowElement.querySelector(".feature-detail-panel");
        const expanded = panel.hidden;
        panel.hidden = !expanded;
        panel.setAttribute("aria-hidden", String(!expanded));
        toggle.setAttribute("aria-expanded", String(expanded));
        toggle.setAttribute("aria-label", expanded ? "Hide feature details" : "Show feature details");
        toggle.textContent = expanded ? "▾" : "▸";
        rowElement.classList.toggle("feature-row-expanded", expanded);
        window.requestAnimationFrame(() => row.normalizeHeight());
      };

      const featureFormatter = cell => {
        const row = cell.getRow();
        const rowData = row.getData();
        const wrapper = document.createElement("div");
        wrapper.className = "feature-name-cell";

        if (hasDetails(rowData)) {
          const toggle = document.createElement("button");
          toggle.type = "button";
          toggle.className = "feature-detail-toggle";
          toggle.setAttribute("aria-expanded", "false");
          toggle.setAttribute("aria-label", "Show feature details");
          toggle.textContent = "▸";
          toggle.addEventListener("click", event => {
            event.stopPropagation();
            toggleDetails(row, toggle);
          });
          wrapper.append(toggle);
        }

        const name = document.createElement("span");
        name.textContent = rowData.feature;
        wrapper.append(name);
        return wrapper;
      };

      const updateCount = rows => {
        countElement.textContent = rows.length + " of " + data.length + " features";
      };

      const table = new Tabulator(tableElement, {
        data,
        groupBy: "category",
        groupStartOpen: false,
        height: "72vh",
        initialSort: [{ column: "feature", dir: "asc" }],
        layout: "fitData",
        placeholder: "No matching features",
        rowFormatter: row => {
          const rowData = row.getData();
          const rowElement = row.getElement();
          if (!hasDetails(rowData) || rowElement.querySelector(".feature-detail-panel")) {
            return;
          }

          const panel = document.createElement("div");
          panel.className = "feature-detail-panel";
          panel.hidden = true;
          panel.setAttribute("aria-hidden", "true");
          panel.textContent = detailText(rowData);
          rowElement.append(panel);
        },
        columns: [
          { title: "Feature", field: "feature", formatter: featureFormatter, headerFilter: "input", minWidth: 220, width: 260 },
          { title: "Category", field: "category", headerFilter: "input", minWidth: 160, width: 190 },
          { title: "LeanCorpus", field: "leancorpus", headerFilter: "input", hozAlign: "center", minWidth: 110, width: 120 },
          { title: "Lucene.NET", field: "luceneNet", headerFilter: "input", hozAlign: "center", minWidth: 110, width: 120 },
          { title: "Lucene (Java)", field: "luceneJava", headerFilter: "input", hozAlign: "center", minWidth: 120, width: 130 }
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