---
adr: ADR023
title: Reader composition uses immutable snapshots and term-order ordinals
date: 2026-08-05
status: Accepted
version-added: 2.2.0
summary: Compose directory readers as immutable snapshots and map sorted values by ordinal term order.
areas: [search, concurrency, docvalues]
---

# ADR023: Reader composition uses immutable snapshots and term-order ordinals

- **Date:** 2026-08-05
- **Status:** Accepted

## Context

Federated search needs several directories to behave as one reader while commits,
refreshes, and merges continue independently. A mixed view assembled by refreshing
components in place could combine generations and change global document IDs while
a query is running. Sorted and sorted-set DocValues also use local ordinal tables,
so equal terms cannot be aggregated safely by local ordinal alone.

## Decision

`MultiReader` opens one immutable `IndexSearcher` snapshot per directory. Input
order defines global document-ID bases and a later commit is visible only through a
new `MultiReader` instance. Search, field sorting, continuation, and federated
facet merging operate on that captured composition.

`OrdinalMap` uses ordinal term order with `StringComparer.Ordinal`. It stores an
immutable global term table and one local-to-global mapping per source reader.
`IndexSearcher.GetOrdinalMap()` maps physical segments and `MultiReader.GetOrdinalMap()`
maps component snapshots. Taxonomy, join, and grouping consumers remain out of scope
until those index structures exist in LeanCorpus.

## Rationale

Immutable composition gives readers the same lifetime and consistency model as an
ordinary committed `IndexSearcher`, and it avoids a writer lock or cross-directory
refresh protocol. Term-order mapping is deterministic, compact after construction,
and matches the existing sorted DocValues representation. Alternatives such as
mutable component refresh or hash-based ordinals would either permit mixed views or
make persisted and distributed ordering harder to reason about.

## Consequences

- A caller must construct a new composition to advance any component snapshot.
- Global document IDs are deterministic for a fixed directory order.
- Global ordinals are stable for the captured source term dictionaries, not across
  later commits with changed terms.
- Federated facet merging uses global ordinals where sorted DocValues exist and
  keeps the existing string fallback for binary or stored values.
