---
uid: api
title: API Reference
---

# API Reference

Browse the namespaces in the left-hand navigation or jump to the common entry points below.

## Common entry points

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter>
- <xref:Rowles.LeanCorpus.Search.Searcher.IndexSearcher>
- <xref:Rowles.LeanCorpus.Document.LeanDocument>

## Namespaces

| Namespace | Purpose |
|---|---|
| `Rowles.LeanCorpus` | Top-level: documents, fields, configuration |
| `Rowles.LeanCorpus.Analysis` | Analysers, tokenisers, token filters |
| `Rowles.LeanCorpus.Codecs` | On-disk format: postings, doc values, stored fields |
| `Rowles.LeanCorpus.Diagnostics` | Metrics, activity sources, slow query log |
| `Rowles.LeanCorpus.Index` | Indexing primitives, segment management, merge policy |
| `Rowles.LeanCorpus.Search` | Queries, scoring, top-N collection |
| `Rowles.LeanCorpus.Store` | Memory-mapped IO and locking |

## Packages

| Package | Description |
|---|---|
| `LeanCorpus` | Core library |
| `LeanCorpus.Compression.LZ4` | LZ4 stored-field compression codec |
| `LeanCorpus.Compression.Snappy` | Snappy stored-field compression codec |
| `LeanCorpus.Compression.Zstandard` | Zstandard stored-field compression codec |
| `LeanCorpus.SourceGen` | Roslyn source generator for typed document mapping |
