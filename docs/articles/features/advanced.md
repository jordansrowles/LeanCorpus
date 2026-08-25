---
title: Advanced and operational features
_description: Compare LeanCorpus vector search, facets, suggestions, geo search, diagnostics, tooling, and deployment features.
---

# Advanced and operational features

Return to the [feature comparison overview](index.md) for status definitions and comparison scope.

## Vector and hybrid search

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Float-vector fields and k-nearest-neighbour queries | ✔ | ❌ | ✔ | HNSW-backed approximate search. |
| Filtered approximate kNN | ✔ | ❌ | ✔ | Pre-filter and post-filter modes. |
| HNSW build configuration | ✔ | ❌ | ◐ | Per-index graph parameters and deterministic seed support. |
| Vector normalisation | ✔ | ❌ | ◐ | Optional index-time L2 normalisation. |
| Int8 and binary vector quantisation | ✔ | ❌ | ✔ | Scalar and binary quantisation across indexing and search. |
| Four-bit and product quantisation | ❌ | ❌ | ✔ | Not currently available. |
| SIMD vector operations | ✔ | ❌ | ◐ | Runtime-vectorised .NET implementation using `Vector<T>`. |
| Reciprocal-rank fusion | ✔ | ❌ | ◐ | Native query form for lexical and vector result fusion. |
| Byte-vector fields and queries | ❌ | ❌ | ✔ | Float vectors are supported instead. |
| Similarity-threshold vector query | ❌ | ❌ | ✔ | No dedicated threshold query. |

## Facets, aggregations, and suggestions

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Term facets | ✔ | ✔ | ✔ | Facet collection over term values. |
| Numeric min, max, sum, average, and count | ✔ | ◐ | ◐ | First-class numeric aggregation requests. |
| Fixed-bucket histograms | ✔ | ❌ | ❌ | LeanCorpus-specific aggregation. |
| Range facets | ❌ | ✔ | ✔ | Numeric and date ranges are not yet available. |
| Taxonomy and hierarchical facets | ❌ | ✔ | ✔ | No taxonomy index. |
| Drill-down and drill-sideways | ❌ | ✔ | ✔ | No corresponding facet query surface. |
| Approximate cardinality aggregation | ❌ | ❌ | ❌ | No built-in HyperLogLog aggregation in these libraries. |
| Percentile aggregation | ❌ | ❌ | ❌ | No built-in HDR histogram or t-digest aggregation in these libraries. |
| Spell checking | ✔ | ✔ | ✔ | `DidYouMeanSuggester` backed by a spell index. |
| Prefix suggestions | ✔ | ✔ | ✔ | FST completion ranked by global document frequency. |
| Analysing suggestions | ✔ | ✔ | ✔ | Applies the selected analyser to completion input. |
| Free-text suggestions | ✔ | ✔ | ✔ | Uses analysed phrase context to suggest the next term. |
| Context-filtered suggestions | ◐ | ✔ | ✔ | LeanCorpus filters analysing suggestions with a query rather than exposing Lucene's context-suggester API. |
| Fuzzy suggestions | ◐ | ✔ | ✔ | LeanCorpus provides edit-distance spelling suggestions rather than Lucene's weighted fuzzy completion API. |

## Geo and spatial search

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Geo point indexing | ◐ | ◐ | ✔ | `GeoPointField` uses a BKD-backed representation with a different API. |
| Bounding-box queries | ✔ | ✔ | ✔ | Latitude and longitude box matching. |
| Distance queries | ✔ | ✔ | ✔ | Radius matching around a point. |
| Geo encoding utilities | ✔ | ✔ | ✔ | Coordinate encoding helpers. |
| Polygon, line, and geo-shape queries | ❌ | ◐ | ✔ | Lucene.NET offers older Spatial4n strategies. |
| Cartesian point and shape search | ❌ | ❌ | ✔ | No XY point or shape API. |

## Search controls and extensions

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Per-query timeout | ✔ | ✔ | ✔ | Cooperative checks between segments. |
| Cancellation tokens | ✔ | ❌ | ◐ | Native .NET cancellation semantics. |
| Per-query memory budget | ✔ | ❌ | ❌ | Caps intermediate result memory. |
| Partial-result signalling | ✔ | ❌ | ❌ | `TopDocs.IsPartial` records budget or timeout truncation. |
| Asynchronous result streaming | ✔ | ❌ | ❌ | `IAsyncEnumerable<ScoreDoc>` search. |
| Segment-by-segment streaming | ✔ | ❌ | ❌ | Supports pipeline consumption before all segments complete. |
| Named ranking profiles and query rules | ✔ | ❌ | ❌ | Immutable relevance configuration, bounded pipelines, filters, boosts, and pins. |
| Snapshot-bound cursor sessions | ✔ | ◐ | ◐ | Opaque cursors remain bound to a retained committed searcher view. |
| Document classification | ❌ | ✔ | ✔ | No classifier module. |
| Percolation and stored-query monitoring | ❌ | ✔ | ✔ | No MemoryIndex or monitor-query equivalent. |

## Diagnostics, tooling, and deployment

| Feature | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| OpenTelemetry traces | ✔ | ❌ | ❌ | `ActivitySource` spans across search and index operations. |
| .NET metrics | ✔ | ❌ | ❌ | Counters and histograms for index maintenance. |
| Slow-query records and search analytics | ✔ | ❌ | ❌ | Bounded in-process diagnostic buffers. |
| Typed index and segment reports | ✔ | ◐ | ◐ | Higher-level reports over file and segment metadata. |
| Index check and inspection CLI | ✔ | ◐ | ◐ | Structured output and JSON support. |
| Backup, compatibility, and migration CLI | ✔ | ❌ | ❌ | Commands over LeanCorpus-native operational APIs. |
| Desktop index browser | ❌ | ✔ | ✔ | Lucene provides Luke. |
| Native AOT compatibility | ✔ | ❌ | ❌ | Trim-safe core without runtime code generation. |
| Source-generated serialisation metadata | ✔ | ❌ | ❌ | Reflection-free `System.Text.Json` metadata. |
