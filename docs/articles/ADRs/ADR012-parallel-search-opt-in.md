# ADR012: Parallel segment search is opt-in

- **Date:** 2026-07-24
- **Status:** Accepted

## Context

The benchmark workload used four and eight segments over 100,000 documents.
Its reported timings did not show a benefit from parallel search, but the
specialised term and Boolean paths bypassed the generic parallel branch. The
benchmark was therefore corrected to use a phrase query, and the default is
kept conservative until a workload proves that opt-in parallelism helps.

## Decision

`IndexSearcherConfig.ParallelSearch` defaults to `false`. Callers that have
measured a benefit on their segment count and query mix can opt in explicitly.

## Rationale

Small segment counts do not provide enough independent work to repay the
parallel scheduling and result-merge overhead on the supported benchmark
workload. Keeping the setting available preserves the option for larger or
more expensive multi-segment queries without imposing that cost on the common
low-latency path.

## Consequences

- New `IndexSearcher` instances search segments sequentially unless configured 
otherwise.
- The parallel benchmark compares the actual generic phrase-query paths rather 
than specialised paths that ignore the setting.
- Applications using larger indexes should benchmark with `ParallelSearch = true` 
before enabling it.
