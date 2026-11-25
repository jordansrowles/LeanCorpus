# Glossary

Terms used across the LeanCorpus documentation, grouped by subject. Each entry links to the page that covers it in detail.

## Index structure and storage

Index
:   A directory holding one or more commits and the segment files they reference. One `IndexWriter` owns it at a time. See [Architecture overview](../architecture.md).

Segment
:   An immutable group of indexed documents with its own term dictionary, postings, stored fields and other files. New documents create new segments; existing segments are never rewritten in place.

Commit
:   A durable, complete view of the index: a `segments_N` file naming the live segments and their deletion state. Readers open commits, not individual segments.

Commit generation
:   The `N` in `segments_N`. Each commit has a higher generation than the one before.

Directory
:   The storage abstraction an index lives in. `MMapDirectory` reads segment files through memory mapping. See [Store and file I/O](../index-management/10-store-and-file-io.md).

Compound file
:   A `.cfs` container that holds a segment's files as logical members, reducing the number of open files. Controlled by `UseCompoundFile`.

Term dictionary
:   The `.dic` file: a finite-state transducer (FST) that maps each field and term to its postings metadata.

Postings
:   The `.pos` file: for each term, the documents that contain it, with frequencies and, where indexed, positions, offsets and payloads.

Stored fields
:   Original field values kept for retrieval (`.fdt` data, `.fdx` index). They are returned with results but not searched.

Norms
:   Per-field length and boost data (`.nrm`, `.fln`) that similarities use to normalise scores.

DocValues
:   Column-oriented per-document values used for sorting, faceting, aggregations and field collapsing without reading postings. See [DocValues](../index-management/07-docvalues.md).

Term vectors
:   Per-document term lists (`.tvd`, `.tvx`), optionally with positions and offsets, used by more-like-this and the faster highlighters. See [Term vectors and payloads](../index-management/09-term-vectors-and-payloads.md).

Payload
:   Optional bytes attached to a term position in the postings.

BKD tree
:   The `.bkd` structure used for numeric and 64-bit integer range search.

Deletion
:   Removing a document marks it in a deletion bitmap (`.del`) for its segment. The space is reclaimed when a merge writes a new segment without it.

Index sort
:   An `IndexSort` that orders documents physically within each flushed and merged segment, so a matching search sort can stop early. See [Sorting](../searching/06-sorting.md).

Codec
:   The versioned binary format of one index file role, such as stored fields or vectors. See [Storage formats](../contributors/storage-formats.md).

CodecKit
:   The LeanCorpus library of composable codecs that frames, versions and checksums binary formats. See [CodecKit](../contributors/codeckit/index.md).

Codec catalogue
:   The immutable set of codecs, including optional compression codecs, that a writer or searcher configuration captures.

Position contract
:   The `PositionContract` marker that a 4.0 flush records in `.seg` metadata to show that position gaps from removed stop words are preserved. Segments without it are legacy. See [Upgrade guide](upgrades.md).

The [architecture overview](../architecture.md) lists every segment file extension.

## Writing and concurrency

IndexWriter
:   The single writer for an index. It analyses documents, buffers them, flushes segments, merges them and publishes commits. It holds an exclusive write lock on the directory.

DWPT
:   Document writer per thread: a per-thread buffer that analyses and holds documents during concurrent indexing. Several DWPTs run in parallel and flush independently. See [Concurrent indexing](../concurrency/02-concurrent-indexing.md).

Flush
:   Writing buffered documents as a new segment. A flush creates files but does not make them visible to committed readers; the next commit does.

Durable commit
:   A commit whose files are synchronised to stable storage before it is acknowledged, so it survives power loss. Controlled by `DurableCommits`.

Backpressure
:   Slowing producers when buffered or queued indexing work exceeds its limits, such as `MaxQueuedBytes`, so memory stays bounded.

Merge
:   Combining several segments into one new segment, dropping deleted documents. Merges run in the background while indexing and commits continue.

Merge policy
:   The rule that selects segments to merge, such as `TieredMergePolicy`, `LogByteSizeMergePolicy` or `NoMergePolicy`.

Deletion policy
:   The rule that decides which older commits to keep, such as `KeepLatestCommitPolicy` or `KeepLastNCommitsPolicy`. See [Snapshots and deletion policies](../concurrency/03-snapshots-and-policies.md).

Snapshot
:   An `IndexSnapshot` that pins one commit so its files cannot be removed while a caller copies or inspects them.

Near real time (NRT)
:   Making new commits searchable soon after they land by refreshing a long-lived searcher in the background, instead of opening a new one per query.

SearcherManager
:   Keeps a current `IndexSearcher` open, swaps in a new one when a newer commit appears, and shares it across concurrent queries. See [Searcher manager](../concurrency/01-searcher-manager.md).

Refresh
:   Opening a searcher on a newer commit. If a refresh fails, the current healthy searcher stays in use. See [Refresh failures](../concurrency/04-refresh-failures.md).

Lease
:   A lifetime handle that keeps a reader, segment or file usable while something still uses it, so files are not deleted underneath an open search.

Physical index runtime
:   The internal Core component that owns one index's directory, optional writer and searcher manager for the Community Server and the Orchard Core provider. See [Physical index lifetimes](../articles/physical-index-lifetimes.md).

## Analysis

Analyser
:   The pipeline that turns field text into indexed terms: a tokeniser followed by token filters. The same analysis must be applied to queries. See [Analysers](../analysis/01-analysers.md).

Tokeniser
:   Splits text into tokens, for example on word boundaries, whitespace or a pattern. See [Tokenisers](../analysis/02-tokenisers.md).

Token filter
:   Transforms, removes or adds tokens after tokenisation, for example lowercasing, stemming or synonyms. See [Token filters](../analysis/03-token-filters.md).

Character filter
:   Rewrites raw text before tokenisation.

Position
:   A token's ordinal place in a field. Phrase, span and intervals queries match on positions.

Position gap
:   The step left in positions when a filter removes a token, such as a stop word, so `said the government` does not match the phrase `"said government"`.

Stop words
:   Common words removed by `StopWordFilter`. See [Stop words and the token budget](../analysis/05-stop-words-and-token-budget.md).

Token budget
:   The per-document token limit, `MaxTokensPerDocument`, with a policy to truncate or reject documents that exceed it.

Stemmer
:   Reduces words to a common stem so related forms match, such as `connected` and `connection`. See [Stemmers](../analysis/04-stemmers.md).

Snowball
:   The stemming algorithm family that LeanCorpus 4.0 runs for English (Porter2), French, German, Spanish, Italian, Portuguese, Dutch, Russian and Arabic.

Porter and Porter2
:   The original English stemming algorithm, still available as `StemmerAnalyser.Porter()`, and its Snowball successor, used by `StemmedAnalyser`.

Hunspell
:   Dictionary-based stemming from `.aff` and `.dic` files. See [Hunspell](../analysis/07-hunspell.md).

Synonym graph
:   A filter that injects synonyms, including multi-word synonyms, as a token graph.

Lexicon
:   Optional language data, such as dictionaries, used by analysis components. See [Lexicons](../analysis/06-lexicons.md).

Analysis identity
:   A versioned name for an analysis pipeline, such as `rowles-analysis/de/3.2.0`. Each commit records the identity of every analysed field, and the writer refuses a change. See [Analysis identities and reindexing](../analysis/01-analysers.md#analysis-identities-and-reindexing).

Rowles.Text
:   The standalone analysis package. Its source is also compiled into LeanCorpus. See [Rowles.Text](../analysis/08-rowles-text.md).

## Searching and scoring

IndexSearcher
:   Searches one committed view of the index. One instance can serve many concurrent queries.

Query
:   A description of which documents match and how they score, such as `TermQuery`, `PhraseQuery` or `BooleanQuery`. See [Query types](../searching/01-query-types.md).

Occur
:   How a clause takes part in a `BooleanQuery`: `Must`, `Should` or `MustNot`. See [Boolean queries](../searching/02-boolean-queries.md).

Filter
:   A clause that narrows matches without changing scores, for example one wrapped in `ConstantScoreQuery` with a score of zero.

Slop
:   How far apart, in positions, phrase terms may be and still match. See [Phrase and proximity](../searching/03-phrase-and-proximity.md).

Span and intervals queries
:   Positional queries for ordered, nested or bounded term arrangements. See [Intervals](../searching/10-intervals.md).

Disjunction max
:   A query that scores a document by its best matching alternative plus a tie-breaker share of the others. See [Disjunction max queries](../searching/08-disjunction-max.md).

Similarity
:   The scoring model. LeanCorpus includes BM25 (the default), BM25+, BM25L, TF-IDF variants and language-model similarities. See [Boosting and scoring](../searching/05-boosting-and-scoring.md).

BM25
:   The default relevance model, combining term frequency, inverse document frequency and field length. Collection-wide statistics keep scores comparable across segments.

Boost
:   A multiplier that raises or lowers the score contribution of a field value or query clause.

Top-N
:   The number of best results a search collects, set by `topN`.

Block-Max WAND
:   An optional optimisation that skips postings blocks whose maximum possible score cannot enter the current top-N.

Score explanation
:   A breakdown of how a document's score was calculated. See [Score explanations](../searching/11-score-explanations.md).

Rescoring
:   Re-ranking the top results of a first query with a second, more expensive scoring step. See [Pagination and rescoring](../searching/12-pagination-and-rescoring.md).

Reciprocal rank fusion (RRF)
:   Merging several result lists by rank rather than score, using `1 / (k + rank)`. See [Reciprocal rank fusion](../advanced/04-rrf.md).

Ranking profile
:   An immutable, named set of relevance settings and query rules, with a fingerprint that identifies the result configuration. See [Ranking profiles and query rules](../searching/12-ranking.md).

Search session
:   A retained view of one commit that keeps results stable while a client pages through them with a cursor. See [Search sessions](../searching/13-search-sessions.md).

Resource controls
:   `SearchOptions` limits, such as a timeout and a maximum result size, that bound what one query can consume. A stopped search returns partial results. See [Per-query resource controls](../searching/09-resource-controls.md).

Query cache
:   Caches complete results for a query fingerprint and `topN`. See [Query cache](../tips/02-query-cache.md).

Facets and aggregations
:   Counts and statistics computed over matching documents, usually from DocValues. See [Faceting](../advanced/11-faceting.md) and [Aggregations](../advanced/01-aggregations.md).

Field collapsing
:   Returning only the best document for each value of a field. See [Field collapsing](../advanced/09-field-collapsing.md).

Block join
:   Indexing child documents directly before their parent so a query can return parents whose children match. See [Block join](../advanced/06-block-join.md).

Highlighting
:   Marking the query terms in returned field text. See [Highlighting](../advanced/03-highlighting.md).

More like this
:   Finding documents similar to a source document from its representative terms. See [More like this](../advanced/07-more-like-this.md).

LINQ provider
:   The `IQueryable<T>` provider that translates expression trees into LeanCorpus queries. See [LINQ queries](../searching/07-linq-queries.md).

## Vector search

Vector field
:   A field holding a fixed-length array of numbers, such as an embedding, stored as Float32 in `.vec` files. See [Vector search](../advanced/05-vector-search.md).

Exact vector search
:   Scoring every eligible vector against the query. Results are exact; cost grows with the number of vectors.

Approximate nearest neighbour (ANN)
:   Searching a graph or other structure that visits only some vectors, trading a little recall for speed.

Vector graph
:   The opt-in Float32 graph in `.vgr` files, built when `BuildHnswOnFlush` is set. It selects candidates, which are then rescored exactly. See [ADR052](../articles/ADRs/ADR052-float32-vector-graph.md).

HNSW
:   Hierarchical navigable small world, the graph design the vector graph follows. Historical `.hnsw` graphs are still read but no longer written.

Vector search route
:   How a segment answered a vector query: `Graph`, or an exact route such as `ExactNoGraph` or `ExactSelectiveFilter`. Diagnostics and metrics record one route per segment and query.

Filtered vector search
:   A vector query restricted by a filter. A very selective filter switches to an exact scan. See [Filtered vector search](../advanced/08-filtered-vector-search.md).

Recall
:   The share of the true nearest neighbours that an approximate search returns.

Int8
:   A historical quantised vector representation in `.vq` files. LeanCorpus searches it as decoded Float32 values and no longer writes it.

BBQ
:   A historical binary-quantised vector representation. Indexes that contain it can be read and searched but are read-only.

## Operations and compatibility

Codec migration
:   Rewriting older codec files into current formats with `IndexCodecMigrator`, which stages, validates and then publishes the result. See [Upgrade guide](upgrades.md).

Compatibility inspection
:   Checking with `IndexCompatibility` what an index contains and whether this version can open, write or migrate it. See [Upgrade guide](upgrades.md) and [Compatibility](compatibility.md).

Index check
:   `IndexValidator.Check`, which checks the latest commit without modifying files and reports issues with `LLIDX` codes. See [Validation and recovery](../index-management/03-validation-recovery.md).

LLIDX code
:   The stable identifier of an index check issue, such as `LLIDX052` for a legacy position contract. See [Validation and recovery](../index-management/03-validation-recovery.md).

Backup and restore
:   Copying one complete commit, its segment files and a manifest of file lengths and checksums, and restoring it as a complete index. See [Backup and restore](../index-management/08-backup-and-restore.md).

Reindex
:   Building a new index from the original source text. Needed when analysis output changes, because codec migration cannot recreate terms or positions.

Native AOT
:   Ahead-of-time compilation to a native executable without the JIT. LeanCorpus is trim-safe and AOT-compatible. See [Native AOT and trimming](native-aot.md).

Source generator
:   `LeanCorpus.SourceGen`, which generates typed document mapping at compile time. See [LeanCorpus.SourceGen](../getting-started/04-source-generated-mapping.md).

Stored-field compression
:   Optional LZ4, Snappy or Zstandard codecs for stored fields. See [Stored field compression](../tips/01-compression.md).

Metrics collector
:   Receives engine measurements: `DefaultMetricsCollector` in process, or `MeterMetricsCollector` through `System.Diagnostics.Metrics`. See [Metrics](../observability/01-metrics.md).

Slow query log
:   A structured log of queries that exceed a time threshold. See [Slow query log](../observability/02-slow-query-log.md).

## Project and tooling

Community Server
:   A local, single-node HTTP and gRPC host for LeanCorpus, released separately as an alpha. See [Community Server](../server/index.md).

ADR
:   Architecture decision record: a numbered document recording a design decision and its reasons. See [Architecture decisions](../articles/ADRs/index.md).

DevOps CLI
:   The repository's `./devops` command for building, testing, benchmarking and building the docs. See [DevOps entry point](../contributors/devops-entry-point.md).

DevOps execution lease
:   The repository-wide lock that stops benchmarks, documentation builds and other exclusive commands from running at the same time.

Run evidence
:   The `run.json`, reports and child exit codes that a DevOps command writes under `artifacts/<category>/runs/<run-id>/`.

DataForge
:   The repository's dataset tooling: deterministic synthetic records for development and benchmarks, and imported reference corpora. See [DataForge datasets](../tips/dataforge.md).

## See also

- [API map](api-map.md)
- [Architecture overview](../architecture.md)
- [Concepts](../concepts/index.md)
