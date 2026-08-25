---
title: LeanCorpus-only capabilities
_description: Capabilities with no direct equivalent in Lucene.NET 4.8 or Java Lucene 10.5.1.
---

# LeanCorpus-only capabilities

These capabilities have no direct equivalent in Lucene.NET 4.8.0-beta00018 or Java Lucene 10.5.1. Lucene may expose lower-level building blocks from which an application could construct comparable behaviour.

This page was audited against the checked-out LeanCorpus source on 25 August 2026. Return to the [feature comparison overview](index.md) for the complete comparison.

## .NET application model

| Capability | LeanCorpus API | Why it differs |
| --- | --- | --- |
| Native AOT support | AOT-safe core and smoke application | Trim-safe paths avoid runtime code generation and reflection-dependent serialisation. |
| Asynchronous document writes | `AddDocumentAsync` and `AddDocumentsAsync` | Native `ValueTask` APIs rather than wrappers around synchronous indexing. |
| Streaming bulk ingestion | `AddDocumentsAsync(IAsyncEnumerable<LeanDocument>)` | Bounded batches consume asynchronous producers directly. |
| Asynchronous result streaming | `SearchAsync` | Results are exposed as `IAsyncEnumerable<ScoreDoc>`. |
| Source-generated document mapping | `LeanDocumentMap<T>` and `[LeanDocument]` | Compile-time field descriptors and mapping code. |
| Typed LINQ queries | `LeanQueryable<T>` | Expressions are translated through the generated document map. |
| JSON document mapping | `JsonDocumentMapper` | Maps `JsonElement` trees into indexed field paths. |
| Source-generated JSON metadata | `LeanCorpusJsonContext` | Reflection-free metadata for LeanCorpus serialisation contracts. |

## Resource controls and indexing safety

| Capability | LeanCorpus API | Why it differs |
| --- | --- | --- |
| Cancellation-token search | `SearchOptions.CancellationToken` | Uses standard .NET cooperative cancellation throughout segment search. |
| Per-query memory budget | `SearchOptions.MaxResultBytes` | Caps intermediate result storage. |
| Explicit partial-result state | `TopDocs.IsPartial` | Records when timeout or budget controls truncate a result. |
| Indexing queue backpressure | `IndexWriterConfig.MaxQueuedDocs` | Blocks producers when the pending document queue is full. |
| Segment-count backpressure | `IndexWriterConfig.MergeThrottleSegments` | Blocks writes until background merging reduces the segment count. |
| First-class schema validation | `IndexSchema` | Enforces field types and required fields at write time. |
| Per-field stored compression | `FieldCompressionPolicy` | Chooses stored-field compression for each field rather than for the whole codec or segment. |

## Operations and diagnostics

| Capability | LeanCorpus API | Why it differs |
| --- | --- | --- |
| Manifest-backed backup and restore | `IndexBackup` | CRC manifests, incremental parent chains, and validated restore are one public workflow. |
| Pre-open compatibility verdict | `IndexCompatibility` | Reports read and write compatibility before opening an index. |
| Structured format inspection | `IndexFormatInspector` | Inventories codec roles, versions, checksums, sidecars, and orphaned files. |
| Staged codec migration | `IndexCodecMigrator` | Plans, rewrites, validates, and publishes format migrations without a full reindex. |
| OpenTelemetry-oriented tracing | `ActivitySource` name `Rowles.LeanCorpus` | Index, search, backup, and migration operations emit .NET activities. |
| .NET metrics integration | `MeterMetricsCollector` | Exposes counters and histograms through `System.Diagnostics.Metrics`. |
| Slow-query and recent-search records | `SlowQueryLog` and `SearchAnalytics` | Bounded in-process diagnostic buffers are provided by the core library. |
| Structured CLI JSON output | `leancorpus-cli --json` | Check, inspect, compatibility, backup, restore, and migration workflows share machine-readable output. |

## Search and ranking extensions

| Capability | LeanCorpus API | Why it differs |
| --- | --- | --- |
| Built-in BM25L and BM25+ | `Bm25LSimilarity` and `Bm25PlusSimilarity` | Additional BM25-family models are included rather than requiring a custom similarity. |
| Additional TF-IDF variants | `TfIdfAugmentedSimilarity`, `TfIdfDoubleNormSimilarity`, and `TfIdfPivotedSimilarity` | Alternative term-frequency and length-normalisation models are built in. |
| Fixed-bucket numeric histograms | `AggregationType.Histogram` | A direct aggregation request is available in the search API. |
| Named ranking profiles and query rules | `RankingProfile` and `QueryRuleSet` | Immutable identities bind similarities, bounded ranking stages, filters, boosts, and pins to a result. |
| Snapshot-bound cursor sessions | `SearchSessionManager` | Versioned cursors remain bound to a retained commit, query, sort, and optional ranking identity. |
