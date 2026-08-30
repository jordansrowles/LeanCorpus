---
title: Core search and indexing
_description: Compare LeanCorpus core document, indexing, storage, query, scoring, sorting, and highlighting features.
---

# Core search and indexing

Return to the [feature comparison overview](index.md) for status definitions and comparison scope.

## Documents and fields

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Document and field model | ✔ | ✔ | ✔ | Text, string, numeric, binary, stored, and vector fields. |
| Stored and indexed field controls | ✔ | ✔ | ✔ | Includes per-field index options and analysis selection. |
| Term vectors, offsets, and payloads | ✔ | ✔ | ✔ | Preserved through flush, merge, migration, and reading. |
| Numeric and sorted DocValues | ✔ | ✔ | ✔ | Numeric, binary, sorted, sorted-numeric, and sorted-set forms. |
| Source-generated typed mapping | ✔ | ❌ | ❌ | Attribute-based, reflection-free mapping with compile-time validation. |
| JSON document mapping | ✔ | ❌ | ❌ | Maps JSON trees and arrays to prefixed fields. |
| IP address fields and queries | ✔ | ❌ | ✔ | IPv4 and IPv6 point, set, and range queries. |
| Byte-vector fields | ❌ | ❌ | ✔ | Float vectors are supported; byte-vector fields are not. |

## Indexing

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Atomic add, update, and delete | ✔ | ✔ | ✔ | Includes delete-by-query and atomic delete-then-add updates. |
| Commit, rollback, and two-phase commit | ✔ | ✔ | ✔ | Prepared commits remain invisible until publication. |
| Concurrent indexing | ✔ | ✔ | ✔ | Multi-threaded document processing and background merges. |
| Asynchronous indexing | ✔ | ❌ | ❌ | `ValueTask` single and bulk APIs. |
| Streaming bulk ingestion | ✔ | ❌ | ❌ | Bounded batches from `IAsyncEnumerable<T>`. |
| Queue and segment backpressure | ✔ | ❌ | ❌ | Configurable limits block producers instead of growing without bound. |
| Schema validation | ✔ | ❌ | ❌ | Enforces field types and required fields during indexing. |
| Index-time sorting | ✔ | ❌ | ✔ | Numeric and string DocValues sort fields. |
| Block-join indexing | ✔ | ✔ | ✔ | Single-level parent and child document blocks. |
| Soft deletes | ✔ | ❌ | ✔ | Currently exposed for term-query selection. |
| Add indexes from another directory | ✔ | ✔ | ✔ | Merges compatible source segments. |
| Merge policies and force merge | ✔ | ✔ | ✔ | Tiered, log-byte-size, no-merge, and explicit force merge. |
| Durable commits | ✔ | ◐ | ◐ | Explicit sync-before-rename behaviour with platform fallback. |

## Storage and index management

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Directory abstraction and memory mapping | ✔ | ✔ | ✔ | Includes immutable mapped segment access. |
| Compound segment files | ✔ | ✔ | ✔ | Optional compound storage for immutable codec members. |
| FST terms, block postings, and BKD trees | ✔ | ✔ | ✔ | Core term, postings, numeric, and geo structures. |
| Pluggable stored-field compression | ✔ | ◐ | ◐ | Built-in Brotli and Deflate plus optional LZ4, Snappy, and Zstandard packages. |
| Per-field compression selection | ✔ | ❌ | ❌ | Chooses stored-field compression by field policy. |
| Near-real-time search and refresh | ✔ | ✔ | ✔ | Reference-counted searcher and reader managers. |
| Query-result cache | ✔ | ❌ | ✔ | Generation-keyed LRU cache per searcher manager. |
| Snapshots and deletion policies | ✔ | ✔ | ✔ | Includes keep-latest and keep-last-N policies. |
| Index validation and recovery | ✔ | ✔ | ✔ | Programmatic validation, checker CLI, and recovery. |
| Backup and incremental restore | ✔ | ◐ | ◐ | CRC manifests and parent-linked incremental chains. |
| Format inspection and compatibility checks | ✔ | ❌ | ❌ | Typed format inventory and pre-open compatibility verdicts. |
| Codec migration | ✔ | ❌ | ❌ | Dry-run planning, staged rewrites, validation, and recoverable publication. |
| Taxonomy reader and writer | ❌ | ✔ | ✔ | Taxonomy faceting is not currently available. |

## Queries and parsing

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Term, Boolean, phrase, prefix, wildcard, fuzzy, range, and drill-down queries | ✔ | ✔ | ✔ | Includes multi-phrase, term-set, and facet-selection forms. |
| Span query family | ✔ | ✔ | ✔ | Near, first, not, or, containment, masking, and multi-term wrappers. |
| Disjunction max and constant score | ✔ | ✔ | ✔ | Native query implementations. |
| More Like This | ✔ | ✔ | ✔ | Query construction from representative text. |
| Combined-field BM25F query | ✔ | ❌ | ✔ | Scores a logical field across several physical fields. |
| Intervals | ✔ | ❌ | ✔ | Ordered, unordered, containing, and related interval rules. |
| Function and function-score queries | ✔ | ◐ | ✔ | Numeric values, constants, scores, and composed arithmetic sources. |
| Query rescoring | ✔ | ✔ | ✔ | Candidate-only second-pass scoring. |
| Search-after pagination | ✔ | ✔ | ✔ | Score and multi-field sort cursors. |
| Classic query parser | ✔ | ✔ | ✔ | Fields, phrases, proximity, ranges, fuzzy terms, prefixes, and boosts. |
| Complex phrase and analysing parsers | ✔ | ✔ | ✔ | Analysis-aware multi-term and span translation. |
| Typed LINQ query provider | ✔ | ❌ | ❌ | Translates expressions through source-generated mappings. |
| Standard, Surround, and XML parsers | ❌ | ✔ | ✔ | Not currently available. |
| Term-based joins and multi-level block joins | ❌ | ✔ | ✔ | Single-level block join is available. |

## Scoring, sorting, grouping, and highlighting

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| BM25 and pluggable similarity | ✔ | ✔ | ✔ | BM25 is the default. |
| TF-IDF and language-model similarities | ✔ | ✔ | ✔ | Includes Dirichlet and Jelinek-Mercer models. |
| Additional BM25 and TF-IDF variants | ✔ | ❌ | ❌ | BM25L, BM25+, pivoted, augmented, and double-normalisation variants. |
| Block-max WAND | ✔ | ◐ | ◐ | LeanCorpus exposes its scorer publicly. |
| Score explanations | ✔ | ✔ | ✔ | Available for term and vector queries. |
| DocValues field sorting | ✔ | ✔ | ✔ | Numeric and string sorts, including multiple fields. |
| Field collapsing | ✔ | ◐ | ◐ | Single-field deduplication by top score or first occurrence. |
| Standard and postings highlighting | ✔ | ✔ | ✔ | Select the implementation for the stored offset source. |
| Fast Vector Highlighter equivalent | ◐ | ✔ | ✔ | Term-vector-based equivalent with a different API. |
| Unified Highlighter equivalent | ◐ | ✔ | ✔ | Hybrid strategy rather than the exact Lucene implementation. |
