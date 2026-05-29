---
uid: api
title: API Reference
---

# API Reference

Welcome to the LeanCorpus API reference. Browse the namespaces in the left-hand
navigation, or jump straight to common entry points below.

Items marked with a lock icon are internal APIs. They are documented for contributors and may
change between releases.

## Common entry points

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter> - write documents to an index.
- <xref:Rowles.LeanCorpus.Search.Searcher.IndexSearcher> - open an index and run queries.
- <xref:Rowles.LeanCorpus.Document.LeanDocument> - the document type used for both indexing and retrieval.

## Namespaces

| Namespace | Purpose |
|---|---|
| `Rowles.LeanCorpus` | Top-level types: documents, fields, configuration. |
| `Rowles.LeanCorpus.Analysis` | Analysers, tokenisers, token filters. |
| `Rowles.LeanCorpus.Codecs` | On-disk format: postings, doc values, stored fields. |
| `Rowles.LeanCorpus.Diagnostics` | Metrics collectors, activity sources, slow query log. |
| `Rowles.LeanCorpus.Index` | Indexing primitives, segment management, merge policy. |
| `Rowles.LeanCorpus.Search` | Queries, scoring, top-N collection. |
| `Rowles.LeanCorpus.Store` | Memory-mapped IO and locking. |

## Packages

| Package | Description |
|---|---|
| `LeanCorpus.Compression.LZ4` | LZ4 stored-field compression codec. |
| `LeanCorpus.Compression.Snappy` | Snappy stored-field compression codec. |
| `LeanCorpus.Compression.Zstandard` | Zstandard stored-field compression codec. |
| `LeanCorpus.SourceGen` | Roslyn source generator for typed document mapping. |