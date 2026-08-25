---
title: Feature comparison
_description: Compare LeanCorpus with Lucene.NET 4.8 and current Java Lucene.
---

# Feature comparison

LeanCorpus covers the core indexing and search capabilities expected from a Lucene-style engine while adding .NET-specific features such as Native AOT support, asynchronous APIs, source-generated mapping, and first-class operational tooling.

The comparison is split into ordinary Markdown tables so it remains useful in GitHub reviews and on the documentation site:

- [Core search and indexing](core.md)
- [Analysis and language support](analysis.md)
- [Advanced and operational features](advanced.md)
- [LeanCorpus-only capabilities](leancorpus-only.md)

## How to read the tables

| Mark | Meaning |
| --- | --- |
| ✔ | A direct equivalent is available. |
| ◐ | A comparable capability is available, but the API or behaviour is not equivalent. |
| ❌ | No equivalent is currently available. |

Lucene.NET refers to 4.8.0-beta00018. Java Lucene refers to 10.5.1. These baselines and the checked-out LeanCorpus source were last audited on 25 August 2026. The tables describe user-visible capability rather than class-for-class API parity.

Baseline documentation: [Lucene.NET 4.8.0](https://lucenenet.apache.org/docs/4.8.0-beta00018/) and [Java Lucene 10.5.1](https://lucene.apache.org/core/10_5_1/).

## Headline comparison

| Capability | LeanCorpus | Lucene.NET 4.8 | Java Lucene | Notes |
| --- | :---: | :---: | :---: | --- |
| Text indexing and search | ✔ | ✔ | ✔ | Documents, fields, postings, term dictionaries, and common query types. |
| Asynchronous indexing | ✔ | ❌ | ❌ | `ValueTask` APIs and bounded `IAsyncEnumerable<T>` ingestion. |
| Streaming search | ✔ | ❌ | ❌ | Asynchronous and segment-by-segment result streams with resource controls. |
| Native AOT | ✔ | ❌ | ❌ | Trim-safe core and reflection-free serialisation paths. |
| Source-generated document mapping | ✔ | ❌ | ❌ | Compile-time field descriptors and schema validation. |
| Vector and hybrid search | ✔ | ❌ | ✔ | HNSW, filtering, quantisation, and reciprocal-rank fusion. |
| Modern query families | ✔ | ◐ | ✔ | Includes intervals, function scoring, rescoring, and disjunction max. |
| Faceting and aggregations | ◐ | ✔ | ✔ | Term facets and numeric aggregations are available; taxonomy and drill-sideways are not. |
| Analysis and language support | ✔ | ✔ | ✔ | Broad core coverage with some specialised Java filters absent. |
| Backup and codec migration | ✔ | ❌ | ❌ | Manifest-backed backup, compatibility checks, inspection, and staged migration. |
| OpenTelemetry and search diagnostics | ✔ | ❌ | ❌ | Activities, metrics, slow-query records, analytics, and typed index reports. |
| Desktop index browser | ❌ | ✔ | ✔ | Lucene provides Luke; LeanCorpus provides CLI inspection instead. |

## Where LeanCorpus differs

LeanCorpus is designed for current .NET deployment rather than API compatibility with Lucene.NET. Public asynchronous APIs, Native AOT, source generation, `System.Diagnostics` telemetry, and explicit timeout and memory controls are part of that design.

Some familiar Lucene capabilities use a different shape. For example, LeanCorpus provides a hybrid highlighter rather than Java Lucene's exact Unified Highlighter, and a `GeoPointField` backed by a BKD tree rather than the `LatLonPoint` API. These are marked comparable where the distinction matters.

The tables are maintained claims, not a generated inventory. When a user-visible capability changes, update the relevant comparison page and link to its guide where a behavioural explanation is useful.
