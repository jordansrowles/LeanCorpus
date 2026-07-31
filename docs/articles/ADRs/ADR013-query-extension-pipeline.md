---
adr: ADR013
title: Custom queries extend the tuned execution pipeline
date: 2026-07-27
status: Accepted
version-added: vNext
summary: Route custom queries through the tuned execution pipeline.
areas: [search, queries, extensibility]
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
- A future general iterator pipeline remains possible, but would require
  benchmarks showing that its flexibility repays its hot-path cost.
