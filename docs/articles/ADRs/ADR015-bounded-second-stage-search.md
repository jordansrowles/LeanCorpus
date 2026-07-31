---
adr: ADR015
title: Pagination and rescoring use bounded collector strategies
date: 2026-07-27
status: Accepted
version-added: vNext
summary: Use bounded collectors for pagination and rescoring.
areas: [search, ranking, performance]
---

# ADR015: Pagination and rescoring use bounded collector strategies

- **Date:** 2026-07-27
- **Status:** Accepted

## Context

`SearchAfter` previously collected every matching document before discarding
the hits before its cursor. `QueryRescorer` similarly collected every match
from the second-pass query even though only first-pass candidates could be
returned. Both behaviours made memory and work grow with the full result set.

Replacing the specialised query dispatch with a new general collector
pipeline would affect every tuned search path. Implementing pagination inside
each query executor would duplicate cursor and heap logic across the engine.

## Decision

`TopNCollector` can optionally delegate to an internal bounded collection
strategy. Normal searches keep their existing heap implementation. Cursor
searches use strategies that compare each hit with a score or field-sort
cursor and retain at most the requested page size. Query rescoring uses a
sorted first-pass candidate table and records second-pass scores only for
those candidates.

A `SearchAfter` cursor belongs to the same immutable searcher snapshot as the
query it continues. Document ID remains the final tie-break for score and
field sorts.

## Rationale

The strategy is confined to explicit pagination and rescoring entry points,
while allowing every built-in query executor to feed the same bounded
collector. It avoids full result materialisation without creating parallel
copies of the query dispatch or replacing the specialised execution model
chosen in ADR013.

The score cursor needs only a bounded `ScoreDoc` heap. Field-sort cursors also
retain a flat value buffer sized by page size and sort count. Rescoring uses a
binary search over first-pass document IDs rather than a dictionary containing
the full second-pass result set.

## Consequences

- Deep pagination retains at most one page of candidates.
- Query rescoring cannot introduce documents outside the first-pass result.
- Cursors must not be reused after refreshing or reopening a searcher.
- Special query families that already require cross-segment coordination keep
  that coordination before the cursor strategy is applied.
- New result-ordering modes must define cursor comparison and document-ID
  tie-breaking before using this strategy.
