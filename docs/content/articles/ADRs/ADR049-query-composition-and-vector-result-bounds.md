---
adr: ADR049
title: "Query composition and vector result bounds"
date: 2026-11-20
status: Accepted
version-added: v4.0.0
summary: "Every executable query keeps its matches when nested, unsupported composition fails loudly, and VectorQuery.TopK selects one global candidate set that every route and enclosing query observes."
areas: [search, queries, vectors]
relates-to: [ADR013, ADR016, ADR037, ADR048]
---

# ADR049: Query composition and vector result bounds

- **Date:** 2026-11-20
- **Status:** Accepted

## Context

Issue #121 reported that `BlockJoinQuery`, `TermInSetQuery`, `PhraseQuery`,
`VectorQuery` and other queries returned no results when used as Boolean
clauses. The Boolean fallback materialised clauses through a sub-query switch
with no default case, so any type without an explicit case silently matched
nothing. The ordinary segment dispatcher also had no default, and handled
neither block join nor the whole-index queries.

Investigating the report found connected defects. The fallback counted later
Must clauses as Should matches and never counted Should clauses alongside
Must, so `MinimumNumberShouldMatch` was wrong. Block-join child Booleans let an
optional Should clause widen a Must result and ignored the Should minimum.
`VectorQuery.TopK` was documented as a maximum but was never applied as a final
cap: the exact scan collected every vector, and HNSW shortlists from each
segment were collected without a combined limit. `VectorQuery` equality also
ignored its filter, `ef` and oversampling settings, so an enabled result cache
could return results for a different execution.

## Decision

**Composition capability.** Every public concrete query is either executable
as a nested clause, or explicitly top-level only. `MoreLikeThisQuery` and
`RrfQuery` coordinate whole-index searches and are top-level only; children of a
root `RrfQuery` are themselves top-level searches. All other built-in queries
are executable nested, including `BlockJoinQuery`. A custom query must rewrite
to built-in queries or supply a weight (ADR013).

A clause that cannot execute where it is placed throws
`NotSupportedException` naming its type. Validation runs at operation
admission, after rewriting and before the result cache, empty-index returns
and any clause short-circuit, so an unsupported clause is rejected even when
an earlier Must clause matches nothing or the index is empty.

**Shared dispatch.** Materialised clauses without a specialised case use the
ordinary segment dispatcher and convert global to local document IDs once.
Specialised term, Boolean streaming, WAND, span, expansion and lease paths are
unchanged. Top-level and nested block join use one segment matcher.

**Boolean counting.** General Boolean and block-join child matching apply the
same rules: Must clauses intersect, MustNot clauses exclude and add no score,
and `MinimumNumberShouldMatch` counts only matching Should clauses. With Must
present, Should clauses are optional unless a positive minimum is set. Without
Must, at least `max(1, minimum)` Should clauses must match. Pure-negative
Booleans match nothing.

**Global vector selection.** `VectorQuery.TopK` selects one candidate set per
query instance and reader snapshot, across every segment and, for a
`MultiReader`, across every component. Selection uses the final vector score,
including query and field boosts, with ties broken by the lower global
document ID. Oversampling and HNSW `ef` govern candidate discovery, not the
final cap, and approximate search keeps its existing quality behaviour.

- Top-level output length is at most `min(topN, TopK, available candidates)`.
- For a bare vector query, `TotalHits` and `Count` report the selected set's
  size. Side collectors, facets, sorting, pagination, streaming and
  asynchronous routes observe the same set.
- A vector Boolean clause intersects, unions or excludes the selected set. A
  sibling filter does not refill it. `VectorQuery.Filter` filters candidates
  before selection and can therefore select a different set.
- `Explain(VectorQuery, int)` reports a zero-score non-match naming the cap for
  a document outside the selected set.
- `topK` must be positive; the constructor throws
  `ArgumentOutOfRangeException` otherwise.

Each operation prepares a vector query's set once, keyed by query reference,
in the existing per-search execution context, and shares it with every
segment and clause, including parallel segment workers. The set is never
reused by a later operation or across a refresh. A failed or interrupted
preparation is discarded. A vector filter that refers back to its own query
fails with `InvalidOperationException`.

**Equality.** `VectorQuery` equality and hashing include the filter, `ef`,
oversampling factor and automatic-oversampling flag, so the result cache never
shares results between different executions.

## Rationale

Sharing the ordinary segment dispatcher removes the duplicated coverage gap
without rewriting the tuned execution paths. Failing loudly turns an invisible
wrong answer into a diagnosable error; per-segment approximation of
`MoreLikeThisQuery` or `RrfQuery` would change their whole-index meaning.

A global selection gives `TopK` one stable meaning regardless of how the index
is divided into segments or components, and it matches the documented
contract. Capping each segment instead would make results depend on segment
layout. Post-filter semantics for sibling clauses keep Boolean composition
algebraic; callers needing filtered nearest neighbours already have
`VectorQuery.Filter`. Reusing the per-search context follows the existing
fuzzy-expansion precedent, which already shares whole-index plans across
parallel segment workers and discards failed plans.

## Consequences

- No executable query silently becomes empty because it is nested.
- Nested `MoreLikeThisQuery` and `RrfQuery`, and custom queries with neither a
  rewrite nor a weight, now throw instead of matching nothing.
- Vector `TotalHits` and `Count` are capped at `TopK`; tests and callers that
  relied on the previous uncapped count observe fewer hits.
- A vector Boolean clause combined with a selective sibling filter can return
  fewer than `TopK` results; use `VectorQuery.Filter` to filter before
  selection.
- Preparing a vector query scans or traverses every segment once per operation
  before segment execution.
- No codec, index-format or public signature change is introduced.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-11-20 | Accepted | Original decision accepted. | `#121` |
