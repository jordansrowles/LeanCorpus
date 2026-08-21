---
adr: ADR028
title: Token graphs remain an analysis concern and flatten before postings
status: Accepted
date: 2026-08-21
version-added: 3.0.0
summary: Preserve token-graph edges through analysis and flatten before postings.
areas: [analysis, indexing, search]
---

# ADR028: Token graphs remain an analysis concern and flatten before postings

## Context

Position increments describe only where a token begins. Shingles and multi-token
synonyms also need an end position. Losing that information creates disconnected
graphs and makes quoted-query analysis depend on emission order.

## Decision

`Token`, span sinks and span filters carry a positive `PositionLength`, defaulting
to one. Graph-producing filters emit ordered start and end edges. `FlattenGraphFilter`
converts graph edges to unit-length token positions before indexing.

`IndexWriter` rejects a non-unit edge rather than silently dropping its end position.
Quoted query analysis enumerates complete graph paths into explicit-position phrase
queries, with a bounded default of 256 paths.

## Consequences

Postings retain their current start-position-only layout. No codec version or index
migration is required. Any future proposal to persist edge lengths must define a new
format version, compatibility reader, migration path and golden vectors before merge.
