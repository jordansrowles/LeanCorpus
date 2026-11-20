---
adr: ADR013
title: "Custom queries extend the tuned execution pipeline"
date: 2026-07-27
status: Accepted
version-added: vNext
last-updated: 2026-11-20
summary: "Route custom queries through the tuned execution pipeline."
areas: [search, queries, extensibility]
relates-to: [ADR015, ADR038, ADR049]
---

# ADR013: Custom queries extend the tuned execution pipeline

- **Date:** 2026-07-27
- **Status:** Accepted

## Context

Query execution previously required every query type to be added to
`IndexSearcher`'s closed dispatch. Replacing that dispatch with a general
iterator pipeline would make custom queries possible, but would also put the
existing specialised term, Boolean, phrase, WAND and SIMD paths behind new
virtual calls and allocations.

## Decision

Built-in queries retain their specialised dispatch. A query can extend the
pipeline in either of two ways:

- `Query.Rewrite()` lowers it to built-in queries, with a limit of 16 rewrite
  rounds.
- `Query.CreateWeight()` supplies an executable built-in approximation and a
  `Scorer` for candidate scores.

`QueryVisitor` provides read-only tree traversal, while `ILeafCollector`
provides optional segment and scorer callbacks without changing the existing
collector contract.

A query that neither rewrites nor supplies a weight is not executable. Search
rejects it with `NotSupportedException`, at the root or as a nested clause,
rather than treating it as matching nothing (ADR049).

## Rationale

Most new query shapes can be expressed as a rewrite and pay no extra cost once
lowered. Queries needing custom scores can reuse a selective built-in
approximation rather than scanning every document. Keeping built-in dispatch
unchanged preserves its allocation and inlining characteristics.

The bounded rewrite loop rejects unstable custom implementations. Requiring a
different approximation query also prevents recursive weight execution.

## Consequences

- Third-party query types no longer require changes to `IndexSearcher`.
- Built-in hot paths retain their existing dispatch and scoring code.
- Custom scorers operate on candidates from their approximation rather than
  replacing segment traversal.
- A custom query with neither extension point fails loudly instead of
  silently matching nothing.
- A future general iterator pipeline remains possible, but would require
  benchmarks showing that its flexibility repays its hot-path cost.

## Changes

| Date | Type | Change | Reference |
| --- | --- | --- | --- |
| 2026-07-27 | Accepted | Original decision accepted. | `9f7e16ce4` |
| 2026-11-20 | Amended | A query with neither a rewrite nor a weight throws `NotSupportedException` instead of matching nothing. | `#121` |
